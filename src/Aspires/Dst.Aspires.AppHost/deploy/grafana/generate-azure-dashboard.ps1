<#
.SYNOPSIS
    Generates the Azure Managed Grafana dashboard (KQL) from the local Grafana dashboard (Prometheus).

.DESCRIPTION
    This script is an offline developer utility used when updating Grafana observability dashboards.
    Locally, dashboards consume Prometheus metrics. In Azure, metrics are exported to Application Insights / Log Analytics (AppMetrics table).
    
    This script:
    1. Reads the local Grafana dashboard JSON (source).
    2. Reads the baseline Azure dashboard JSON (destination) to preserve fine-tuned Azure KQL queries for unchanged panels.
    3. Replaces panel datasources with the Azure Monitor datasource (__DS_UID__).
    4. Translates Prometheus metrics into Azure Log Analytics (KQL) queries for modified or newly mapped panels.
    5. Applies Azure-specific panel transformations and variable templating for cell selection (__CELL_OPTIONS__).
    6. Outputs the formatted JSON to the destination path.

.WHERE IS IT USED
    Used by developers from the command line when updating dashboards:
    .\deploy\grafana\generate-azure-dashboard.ps1 observability\grafana\dashboards\cell-overview.json deploy\grafana\cell-overview.azure.json
    The generated output (cell-overview.azure.json) is committed to git and imported into Azure Managed Grafana
    by deploy/grafana_dashboard.bicep during infrastructure provisioning.

.DEPENDENCIES
    - PowerShell 5.1 or PowerShell Core 7+ (pwsh)
    - No external modules or Python runtime required.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0, HelpMessage = "Path to the local source dashboard JSON (e.g., cell-overview.json)")]
    [string]$SourcePath,

    [Parameter(Mandatory = $true, Position = 1, HelpMessage = "Path to the existing baseline Azure dashboard JSON (e.g., cell-overview.azure.json)")]
    [string]$DestinationPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "Source dashboard file not found: $SourcePath"
}
if (-not (Test-Path -LiteralPath $DestinationPath)) {
    throw "Existing Azure dashboard baseline not found: $DestinationPath. The existing Azure JSON is required as a baseline to retain Azure-only query fixes."
}

# Load source and previous baseline
$d = Get-Content -LiteralPath $SourcePath -Raw -Encoding utf8 | ConvertFrom-Json
$previous = Get-Content -LiteralPath $DestinationPath -Raw -Encoding utf8 | ConvertFrom-Json

# Index previous panels by ID
$previousPanels = @{}
foreach ($panel in $previous.panels) {
    if ($null -ne $panel.id) {
        $previousPanels[[int]$panel.id] = $panel
    }
}

$DS = [ordered]@{
    type = "grafana-azure-monitor-datasource"
    uid  = "__DS_UID__"
}

$M = 'AppMetrics | where $__timeFilter(TimeGenerated)'
$API = "(AppRoleName endswith '-web-api-ca' or AppRoleName endswith '/dst-web-api')"
$SILO = "(AppRoleName endswith '-web-orleans-silo-ca' or AppRoleName endswith '/dst-web-orleans-silo')"
$NOT_HEALTH = "tostring(Properties['http.route']) !in ('/health', '/alive')"
$USER_GRAIN = "g !startswith 'sys.' and g !in ('dashboard', 'management')"
$GRAIN = "$M | where Name == 'orleans-catalog-activations' | extend g = tostring(Properties['grain_type'])"

function New-Target([string]$query, [string]$fmt = "time_series", [string]$ref = "A") {
    return [ordered]@{
        refId              = $ref
        queryType          = "Azure Log Analytics"
        datasource         = $DS
        azureLogAnalytics  = [ordered]@{
            resultFormat = $fmt
            resources    = @('$cell')
            query        = $query
        }
    }
}

function Get-ActiveByBin([string]$whereClause) {
    return "$GRAIN | where $whereClause | summarize v = max(Sum) by bin(TimeGenerated, 1m), AppRoleInstance, g | summarize Active = sum(v) by TimeGenerated | where TimeGenerated < bin(now(), 1m) - 2m"
}

function Get-TableQuery([string]$role) {
    return "let win = 5m; " +
        "AppMetrics " +
        "| where TimeGenerated > ago(win) " +
        "| where $role " +
        "| where Name in ('dotnet.process.cpu.time', 'dotnet.process.cpu.count', 'dotnet.process.memory.working_set', 'dotnet.gc.last_collection.heap.size', 'dst.process.thread_pool.thread.count', 'dst.process.thread_pool.queue.length') " +
        "| summarize arg_max(TimeGenerated, Sum), cpuSum = sum(Sum) by AppRoleInstance, Name, gen = tostring(Properties['gc.heap.generation']) " +
        "| summarize " +
            "cpuSec = sumif(cpuSum, Name == 'dotnet.process.cpu.time'), " +
            "cores = take_anyif(Sum, Name == 'dotnet.process.cpu.count'), " +
            "Mem = take_anyif(Sum, Name == 'dotnet.process.memory.working_set'), " +
            "Heap = sumif(Sum, Name == 'dotnet.gc.last_collection.heap.size'), " +
            "Thr = take_anyif(Sum, Name == 'dst.process.thread_pool.thread.count'), " +
            "Q = take_anyif(Sum, Name == 'dst.process.thread_pool.queue.length') " +
            "by Instance = AppRoleInstance " +
        "| project Instance, " +
            "CPU = 100.0 * cpuSec / (toreal(win / 1s) * max_of(cores, 1.0)), " +
            "['Memory (working set)'] = Mem, " +
            "['GC heap'] = Heap, " +
            "Threads = Thr, " +
            "['Queued work'] = iff(isnotnull(Q), max_of(Q, 0.0), real(null)) " +
        "| order by Instance asc"
}

$createdQuery = $GRAIN.Replace('orleans-catalog-activations', 'orleans-catalog-activation-created')

$Q = @{
    10 = @("table", @("AppMetrics | where TimeGenerated > ago(5m) | where $API | summarize Instances = dcount(AppRoleInstance)"))
    11 = @("table", @("AppMetrics | where TimeGenerated > ago(5m) | where $SILO | summarize Instances = dcount(AppRoleInstance)"))
    12 = @("table", @("AppMetrics | where TimeGenerated > ago(5m) | where Name == 'http.server.request.duration' and $API | where $NOT_HEALTH | summarize v = sum(ItemCount) / 300.0"))
    13 = @("table", @("AppMetrics | where TimeGenerated > ago(5m) | where Name == 'http.server.request.duration' | where toint(Properties['http.response.status_code']) >= 500 | summarize v = sum(ItemCount)"))
    14 = @("table", @("$GRAIN | where $USER_GRAIN | where TimeGenerated > ago(5m) | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance, g | summarize v = sum(Sum)"))
    15 = @("table", @("AppMetrics | where TimeGenerated > ago(5m) | where Name == 'http.server.request.duration' and $API | where $NOT_HEALTH | summarize v = sum(Sum) / sum(ItemCount)"))
    1  = @("time_series", @(
        "$M | where Name == 'http.server.request.duration' | where $NOT_HEALTH | extend status = tostring(Properties['http.response.status_code']) | where status !startswith '5' | summarize ['Requests per second'] = sum(ItemCount) / 60.0 by bin(TimeGenerated, 1m), ['HTTP status'] = status | order by TimeGenerated asc",
        "$M | where Name == 'http.server.request.duration' | extend status = toint(Properties['http.response.status_code']) | summarize ['Failures per second'] = sumif(ItemCount, status >= 500) / 60.0, ['Avg response time'] = sumif(Sum, $API and $NOT_HEALTH) / sumif(ItemCount, $API and $NOT_HEALTH) by bin(TimeGenerated, 1m) | order by TimeGenerated asc"
    ))
    6  = @("time_series", @("$M | summarize Instances = dcount(AppRoleInstance) by bin(TimeGenerated, 1m), ['Build (revision)'] = AppVersion | order by TimeGenerated asc"))
    4  = @("time_series", @(
        "let active = $(Get-ActiveByBin $USER_GRAIN) | order by TimeGenerated asc | extend delta = Active - prev(Active); " +
        "let created = $createdQuery | where $USER_GRAIN | summarize Created = sum(Sum) by bin(TimeGenerated, 1m); " +
        "let latency = $M | where Name == 'orleans-app-requests-latency' | summarize ['Avg grain call latency'] = sum(Sum) / sum(ItemCount) by bin(TimeGenerated, 1m); " +
        "active | join kind=leftouter created on TimeGenerated | join kind=leftouter latency on TimeGenerated " +
        "| project TimeGenerated, Active, Deactivated = max_of(coalesce(Created, 0.0) - coalesce(delta, 0.0), 0.0), ['Avg grain call latency'] " +
        "| order by TimeGenerated asc"
    ))
    5  = @("time_series", @("$GRAIN | where not($USER_GRAIN) | summarize v = max(Sum) by bin(TimeGenerated, 1m), AppRoleInstance, ['Grain type'] = g | summarize Activations = sum(v) by TimeGenerated, ['Grain type'] | order by TimeGenerated asc"))
    30 = @("table", @("$GRAIN | where $USER_GRAIN | where TimeGenerated > ago(5m) | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance, g | summarize Active = sum(Sum) by ['Grain type'] = g | top 10 by Active desc"))
    31 = @("table", @("$GRAIN | where $USER_GRAIN | where TimeGenerated > ago(5m) | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance, g | summarize Active = sum(Sum) by Instance = AppRoleInstance"))
    33 = @("time_series", @(
        "let m = AppMetrics | where `$__timeFilter(TimeGenerated) | where $SILO; " +
        "let readOps = m | where Name == 'orleans-storage-read-latency' | summarize Value = sum(ItemCount) / 60.0 by bin(TimeGenerated, 1m), Storage = tostring(Properties['provider_type_name']) | extend Series = strcat('Read ops ', Storage); " +
        "let writeOps = m | where Name == 'orleans-storage-write-latency' | summarize Value = sum(ItemCount) / 60.0 by bin(TimeGenerated, 1m), Storage = tostring(Properties['provider_type_name']) | extend Series = strcat('Write ops ', Storage); " +
        "let readLatency = m | where Name == 'orleans-storage-read-latency' | summarize Value = sum(Sum) / sum(ItemCount) by bin(TimeGenerated, 1m), Storage = tostring(Properties['provider_type_name']) | extend Series = strcat('Read latency ', Storage); " +
        "let writeLatency = m | where Name == 'orleans-storage-write-latency' | summarize Value = sum(Sum) / sum(ItemCount) by bin(TimeGenerated, 1m), Storage = tostring(Properties['provider_type_name']) | extend Series = strcat('Write latency ', Storage); " +
        "union readOps, writeOps, readLatency, writeLatency | project TimeGenerated, Value, Series | order by TimeGenerated asc"
    ))
    2  = @("table", @(Get-TableQuery $API))
    3  = @("table", @(Get-TableQuery $SILO))
}

# Add changed/new panel IDs here to force updating their Azure Monitor query in Q.
$AZURE_QUERY_OVERRIDES = [System.Collections.Generic.HashSet[int]]::new([int[]]@(2, 3, 33))

$AZURE_DESCRIPTIONS = @{
    2  = "One row per Web API replica reporting in the last five minutes. CPU is a five-minute average normalized by the runtime-reported processor count, not the ACA vCPU allocation. Memory is the OS working set; GC heap is the managed heap after the last collection. Threads and queued work are current thread-pool gauges (require redeployment); missing samples stay blank, not zero."
    3  = "One row per silo replica reporting in the last five minutes. CPU is a five-minute average normalized by the runtime-reported processor count, not the ACA vCPU allocation. Memory is the OS working set; GC heap is the managed heap after the last collection. Threads and queued work are current thread-pool gauges (require redeployment); missing samples stay blank, not zero."
    30 = "The 10 application grain types with the most active instances (in memory) right now, summed across replicas and builds. Labels show only the grain type."
    31 = "Distribution of currently active application grains across silo replicas (latest samples within 5 minutes). Each slice represents one silo; values are grain counts. The legend includes idle silos with 0; if all counts are zero, there are no slices. System grains are excluded."
}

foreach ($p in $d.panels) {
    if ($p.type -eq "text") {
        continue
    }

    $p.datasource = $DS
    $panelId = [int]$p.id
    $oldPanel = $previousPanels[$panelId]

    if ($null -ne $oldPanel -and -not $AZURE_QUERY_OVERRIDES.Contains($panelId)) {
        # Clone old targets
        $targets = @($oldPanel.targets)
        $targetsJson = ,$targets | ConvertTo-Json -Depth 50
        $clonedTargets = @($targetsJson | ConvertFrom-Json)
        foreach ($target in $clonedTargets) {
            $target.datasource = $DS
        }
        $p.targets = [object[]]@($clonedTargets)
    } else {
        $qEntry = $Q[$panelId]
        $fmt = $qEntry[0]
        $queries = $qEntry[1]
        $newTargets = [System.Collections.ArrayList]::new()
        for ($i = 0; $i -lt $queries.Length; $i++) {
            $refLetter = [char](65 + $i)
            [void]$newTargets.Add((New-Target -query $queries[$i] -fmt $fmt -ref ([string]$refLetter)))
        }
        $p.targets = [object[]]$newTargets.ToArray()
    }

    if ($AZURE_DESCRIPTIONS.ContainsKey($panelId)) {
        $p | Add-Member -MemberType NoteProperty -Name description -Value $AZURE_DESCRIPTIONS[$panelId] -Force
    }

    if ($panelId -in @(10, 11, 12, 13, 14, 15)) {
        if ($null -eq $p.options) {
            $p | Add-Member -MemberType NoteProperty -Name options -Value ([PSCustomObject]@{}) -Force
        }
        if ($null -eq $p.options.reduceOptions) {
            $p.options | Add-Member -MemberType NoteProperty -Name reduceOptions -Value ([PSCustomObject]@{}) -Force
        }
    }

    if ($panelId -eq 2 -or $panelId -eq 3) {
        $keepTransforms = @()
        if ($null -ne $p.transformations) {
            foreach ($tx in $p.transformations) {
                if ($tx.id -eq "organize") {
                    if ($null -eq $tx.options) {
                        $tx | Add-Member -MemberType NoteProperty -Name options -Value ([PSCustomObject]@{}) -Force
                    }
                    $tx.options | Add-Member -MemberType NoteProperty -Name renameByName -Value ([PSCustomObject]@{}) -Force
                    $keepTransforms += $tx
                }
            }
        }
        $p | Add-Member -MemberType NoteProperty -Name transformations -Value $keepTransforms -Force
    }

    if ($panelId -eq 31) {
        if ($null -eq $oldPanel) {
            if ($null -eq $p.options) {
                $p | Add-Member -MemberType NoteProperty -Name options -Value ([PSCustomObject]@{}) -Force
            }
            $p.options | Add-Member -MemberType NoteProperty -Name reduceOptions -Value ([PSCustomObject]@{
                calcs  = @("lastNotNull")
                fields = ""
                values = $false
            }) -Force
        }
        $p | Add-Member -MemberType NoteProperty -Name transformations -Value @(
            [PSCustomObject]@{
                id      = "rowsToFields"
                options = [PSCustomObject]@{
                    mappings = @(
                        [PSCustomObject]@{ fieldName = "Instance"; handlerKey = "field.name" },
                        [PSCustomObject]@{ fieldName = "Active"; handlerKey = "field.value" }
                    )
                }
            }
        ) -Force
    }

    if ($panelId -eq 30) {
        $keepTransforms = @()
        if ($null -ne $p.transformations) {
            foreach ($tx in $p.transformations) {
                if ($tx.id -eq "sortBy") {
                    $keepTransforms += $tx
                }
            }
        }
        $p | Add-Member -MemberType NoteProperty -Name transformations -Value $keepTransforms -Force
    }

    if ($panelId -eq 6) {
        if ($null -eq $p.fieldConfig) {
            $p | Add-Member -MemberType NoteProperty -Name fieldConfig -Value ([PSCustomObject]@{}) -Force
        }
        if ($null -eq $p.fieldConfig.defaults) {
            $p.fieldConfig | Add-Member -MemberType NoteProperty -Name defaults -Value ([PSCustomObject]@{}) -Force
        }
        $p.fieldConfig.defaults | Add-Member -MemberType NoteProperty -Name unit -Value "short" -Force
        if ($null -ne $oldPanel -and $null -ne $oldPanel.fieldConfig -and $null -ne $oldPanel.fieldConfig.defaults) {
            $displayName = $oldPanel.fieldConfig.defaults.displayName
            if ($null -ne $displayName) {
                $p.fieldConfig.defaults | Add-Member -MemberType NoteProperty -Name displayName -Value $displayName -Force
            }
        }
    }
}

$d.title = "Cell overview (Azure)"
$d.uid = "dst-cell-overview"
$d | Add-Member -MemberType NoteProperty -Name refresh -Value "5m" -Force
$d | Add-Member -MemberType NoteProperty -Name time -Value ([PSCustomObject]@{
    from = "now-15m"
    to   = "now"
}) -Force
$d.templating = [PSCustomObject]@{
    list = @(
        [PSCustomObject]@{
            name       = "cell"
            label      = "Cell"
            type       = "custom"
            query      = "__CELL_OPTIONS__"
            current    = [PSCustomObject]@{}
            options    = @()
            multi      = $false
            includeAll = $false
        }
    )
}

if ($d.PSObject.Properties['id']) {
    $d.PSObject.Properties.Remove('id')
}

$jsonOutput = $d | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($DestinationPath, $jsonOutput + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Successfully generated Azure dashboard at: $DestinationPath"
