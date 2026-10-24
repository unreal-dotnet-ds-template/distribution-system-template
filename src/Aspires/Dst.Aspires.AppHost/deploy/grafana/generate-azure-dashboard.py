import copy
import json
import os
import sys

src, dst = sys.argv[1], sys.argv[2]
d = json.load(open(src, encoding="utf-8"))
if not os.path.exists(dst):
    raise FileNotFoundError(f"Existing Azure dashboard baseline not found: {dst}")
previous = json.load(open(dst, encoding="utf-8"))
previous_panels = {p.get("id"): p for p in previous["panels"]}

DS = {"type": "grafana-azure-monitor-datasource", "uid": "__DS_UID__"}
M = "AppMetrics | where $__timeFilter(TimeGenerated)"
API = "AppRoleName endswith '/dst-web-api'"
SILO = "(AppRoleName endswith '-web-orleans-silo-ca' or AppRoleName endswith '/dst-web-orleans-silo')"
NOT_HEALTH = "tostring(Properties['http.route']) !in ('/health', '/alive')"
USER_GRAIN = "g !startswith 'sys.' and g !in ('dashboard', 'management')"
GRAIN = f"{M} | where Name == 'orleans-catalog-activations' | extend g = tostring(Properties['grain_type'])"
LATEST = "5m"


def t(query, fmt="time_series", ref="A"):
    return {
        "refId": ref,
        "queryType": "Azure Log Analytics",
        "datasource": DS,
        "azureLogAnalytics": {"resultFormat": fmt, "resources": ["$cell"], "query": query},
    }


def active_by_bin(where):
    return (f"{GRAIN} | where {where} | summarize v = max(Sum) by bin(TimeGenerated, 1m), AppRoleInstance, g"
            " | summarize Active = sum(v) by TimeGenerated | where TimeGenerated < bin(now(), 1m) - 2m")


def table_query(role):
    return (
        f"let win = 5m; let m = AppMetrics | where TimeGenerated > ago(win) | where {role}; "
        "let cpu = m | where Name == 'dotnet.process.cpu.time' | summarize cpuSec = sum(Sum) by AppRoleInstance; "
        "let cores = m | where Name == 'dotnet.process.cpu.count' | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance | project AppRoleInstance, cores = Sum; "
        "let mem = m | where Name == 'dotnet.process.memory.working_set' | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance | project AppRoleInstance, Mem = Sum; "
        "let heap = m | where Name == 'dotnet.gc.last_collection.heap.size' | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance, gen = tostring(Properties['gc.heap.generation']) | summarize Heap = sum(Sum) by AppRoleInstance; "
        "let thr = m | where Name == 'dotnet.thread_pool.thread.count' | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance | project AppRoleInstance, Thr = Sum; "
        "let q = m | where Name == 'dotnet.thread_pool.queue.length' | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance | project AppRoleInstance, Q = Sum; "
        "cpu | join kind=leftouter cores on AppRoleInstance | join kind=leftouter mem on AppRoleInstance "
        "| join kind=leftouter heap on AppRoleInstance | join kind=leftouter thr on AppRoleInstance | join kind=leftouter q on AppRoleInstance "
        "| project Instance = AppRoleInstance, CPU = 100.0 * cpuSec / (toreal(win / 1s) * max_of(cores, 1.0)), "
        "['Memory (working set)'] = Mem, ['GC heap'] = Heap, Threads = Thr, ['Queued work'] = max_of(Q, 0.0) | order by Instance asc"
    )


Q = {
    10: ("table", f"AppMetrics | where TimeGenerated > ago(5m) | where {API} | summarize Instances = dcount(AppRoleInstance)"),
    11: ("table", f"AppMetrics | where TimeGenerated > ago(5m) | where {SILO} | summarize Instances = dcount(AppRoleInstance)"),
    12: ("table", f"AppMetrics | where TimeGenerated > ago(5m) | where Name == 'http.server.request.duration' and {API} | where {NOT_HEALTH} | summarize v = sum(ItemCount) / 300.0"),
    13: ("table", f"AppMetrics | where TimeGenerated > ago(5m) | where Name == 'http.server.request.duration' | where toint(Properties['http.response.status_code']) >= 500 | summarize v = sum(ItemCount)"),
    14: ("table", f"{GRAIN} | where {USER_GRAIN} | where TimeGenerated > ago(5m) | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance, g | summarize v = sum(Sum)"),
    15: ("table", f"AppMetrics | where TimeGenerated > ago(5m) | where Name == 'http.server.request.duration' and {API} | where {NOT_HEALTH} | summarize v = sum(Sum) / sum(ItemCount)"),
    1: ("time_series", [
        f"{M} | where Name == 'http.server.request.duration' | where {NOT_HEALTH} | extend status = tostring(Properties['http.response.status_code']) | where status !startswith '5' | summarize ['Requests per second'] = sum(ItemCount) / 60.0 by bin(TimeGenerated, 1m), ['HTTP status'] = status | order by TimeGenerated asc",
        f"{M} | where Name == 'http.server.request.duration' | extend status = toint(Properties['http.response.status_code']) | summarize ['Failures per second'] = sumif(ItemCount, status >= 500) / 60.0, ['Avg response time'] = sumif(Sum, {API} and {NOT_HEALTH}) / sumif(ItemCount, {API} and {NOT_HEALTH}) by bin(TimeGenerated, 1m) | order by TimeGenerated asc",
    ]),
    6: ("time_series", f"{M} | summarize Instances = dcount(AppRoleInstance) by bin(TimeGenerated, 1m), ['Build (revision)'] = AppVersion | order by TimeGenerated asc"),
    4: ("time_series", (
        f"let active = {active_by_bin(USER_GRAIN)} | order by TimeGenerated asc | extend delta = Active - prev(Active); "
        f"let created = {GRAIN.replace('orleans-catalog-activations', 'orleans-catalog-activation-created')} | where {USER_GRAIN} | summarize Created = sum(Sum) by bin(TimeGenerated, 1m); "
        f"let latency = {M} | where Name == 'orleans-app-requests-latency' | summarize ['Avg grain call latency'] = sum(Sum) / sum(ItemCount) by bin(TimeGenerated, 1m); "
        "active | join kind=leftouter created on TimeGenerated | join kind=leftouter latency on TimeGenerated "
        "| project TimeGenerated, Active, Deactivated = max_of(coalesce(Created, 0.0) - coalesce(delta, 0.0), 0.0), ['Avg grain call latency'] "
        "| order by TimeGenerated asc")),
    5: ("time_series", f"{GRAIN} | where not({USER_GRAIN}) | summarize v = max(Sum) by bin(TimeGenerated, 1m), AppRoleInstance, ['Grain type'] = g | summarize Activations = sum(v) by TimeGenerated, ['Grain type'] | order by TimeGenerated asc"),
    30: ("table", f"{GRAIN} | where {USER_GRAIN} | where TimeGenerated > ago(5m) | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance, g | summarize Active = sum(Sum) by ['Grain type'] = g | top 10 by Active desc"),
    31: ("table", f"{GRAIN} | where {USER_GRAIN} | where TimeGenerated > ago(5m) | summarize arg_max(TimeGenerated, Sum) by AppRoleInstance, g | summarize Active = sum(Sum) by Instance = AppRoleInstance"),
    33: ("time_series", f"let m = AppMetrics | where $__timeFilter(TimeGenerated) | where {SILO}; "
        "let readOps = m | where Name == 'orleans-storage-read-latency' | summarize Value = sum(ItemCount) / 60.0 by bin(TimeGenerated, 1m), Storage = tostring(Properties['provider_type_name']) | extend Series = strcat('Read ops ', Storage); "
        "let writeOps = m | where Name == 'orleans-storage-write-latency' | summarize Value = sum(ItemCount) / 60.0 by bin(TimeGenerated, 1m), Storage = tostring(Properties['provider_type_name']) | extend Series = strcat('Write ops ', Storage); "
        "let readLatency = m | where Name == 'orleans-storage-read-latency' | summarize Value = sum(Sum) / sum(ItemCount) by bin(TimeGenerated, 1m), Storage = tostring(Properties['provider_type_name']) | extend Series = strcat('Read latency ', Storage); "
        "let writeLatency = m | where Name == 'orleans-storage-write-latency' | summarize Value = sum(Sum) / sum(ItemCount) by bin(TimeGenerated, 1m), Storage = tostring(Properties['provider_type_name']) | extend Series = strcat('Write latency ', Storage); "
        "union readOps, writeOps, readLatency, writeLatency | project TimeGenerated, Value, Series | order by TimeGenerated asc"),
    2: ("table", table_query(API)),
    3: ("table", table_query(SILO)),
}

# Add changed/new panel ids here and update their Azure Monitor query in Q.
AZURE_QUERY_OVERRIDES = {33}
AZURE_DESCRIPTIONS = {
    2: "One row per Web API replica reporting in the last five minutes. CPU is a five-minute average normalized by the runtime-reported processor count, not the ACA vCPU allocation. Memory is the OS working set; GC heap is the managed heap after the last collection. Threads and queued work are current thread-pool gauges (require redeployment); missing samples stay blank, not zero.",
    3: "One row per silo replica reporting in the last five minutes. CPU is a five-minute average normalized by the runtime-reported processor count, not the ACA vCPU allocation. Memory is the OS working set; GC heap is the managed heap after the last collection. Threads and queued work are current thread-pool gauges (require redeployment); missing samples stay blank, not zero.",
    30: "The 10 application grain types with the most active instances (in memory) right now, summed across replicas and builds. Labels show only the grain type.",
    31: "Distribution of currently active application grains across silo replicas (latest samples within 5 minutes). Each slice represents one silo; values are grain counts. The legend includes idle silos with 0; if all counts are zero, there are no slices. System grains are excluded.",
}

for p in d["panels"]:
    if p["type"] == "text":
        continue
    p["datasource"] = DS
    old_panel = previous_panels.get(p["id"])
    if old_panel and p["id"] not in AZURE_QUERY_OVERRIDES:
        p["targets"] = copy.deepcopy(old_panel["targets"])
        for target in p["targets"]:
            target["datasource"] = DS
    else:
        fmt, q = Q[p["id"]]
        qs = q if isinstance(q, list) else [q]
        p["targets"] = [t(x, fmt, chr(65 + i)) for i, x in enumerate(qs)]
    if p["id"] in AZURE_DESCRIPTIONS:
        p["description"] = AZURE_DESCRIPTIONS[p["id"]]
    if p["id"] in (10, 11, 12, 13, 14, 15):
        p.setdefault("options", {}).setdefault("reduceOptions", {})
    if p["id"] == 2 or p["id"] == 3:
        p["transformations"] = [x for x in p.get("transformations", []) if x["id"] == "organize"]
        for x in p["transformations"]:
            x["options"]["renameByName"] = {}
    if p["id"] == 31:
        if not old_panel:
            p["options"]["reduceOptions"] = {"calcs": ["lastNotNull"], "fields": "", "values": False}
        p["transformations"] = [{
            "id": "rowsToFields",
            "options": {
                "mappings": [
                    {"fieldName": "Instance", "handlerKey": "field.name"},
                    {"fieldName": "Active", "handlerKey": "field.value"},
                ],
            },
        }]
    if p["id"] == 30:
        p["transformations"] = [x for x in p["transformations"] if x["id"] == "sortBy"]
    if p["id"] == 6:
        p["fieldConfig"]["defaults"]["unit"] = "short"
        if old_panel:
            display_name = old_panel.get("fieldConfig", {}).get("defaults", {}).get("displayName")
            if display_name:
                p["fieldConfig"]["defaults"]["displayName"] = display_name

d["title"] = "Cell overview (Azure)"
d["uid"] = "dst-cell-overview"
d["templating"] = {"list": [{
    "name": "cell", "label": "Cell", "type": "custom", "query": "__CELL_OPTIONS__",
    "current": {}, "options": [], "multi": False, "includeAll": False,
}]}
d.pop("id", None)
json.dump(d, open(dst, "w", encoding="utf-8"), indent=2)
