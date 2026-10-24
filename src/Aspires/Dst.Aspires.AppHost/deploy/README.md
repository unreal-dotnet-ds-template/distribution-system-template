# Azure deployment

All Bicep under `../infra` is **generated** from the AppHost (`aspire publish`) by the official build pipeline and is not committed.
Only this folder is hand-written.

| File | Scope | Purpose |
|---|---|---|
| `main.bicep` | subscription | Entry point. Holds the cell catalog, loops over the requested cells and handles the mode. |
| `main.bicepparam` | | Parameters; each value is read from an environment variable so the pipeline can override it. |
| `global_resources.bicep` | resource group | Global container registry (default Basic; Premium geo-replicates to the cell regions) and, via `front_door.bicep`, Azure Front Door (default Standard; Premium adds managed WAF rules) with one origin per cell. Deployed twice: registry first, Front Door after the cells exist. |
| `front_door.bicep` | resource group | Front Door profile, WAF and routing (called by `global_resources.bicep`). |
| `cell_resources.bicep` | subscription | Wraps the Aspire-generated `infra/main.bicep` (resource group, VNet, ACA, storage + private endpoints, Application Insights, identities). |
| `cell_apps.bicep` | resource group | Web API and Orleans silo container apps of one cell (both apps in one file). |
| `grafana.bicep`, `grafana_access.bicep`, `grafana_dashboard.bicep` | resource group | Azure Managed Grafana (enabled by default): instance, read roles on every cell, and a deployment script that imports `grafana/cell-overview.azure.json`. |

## Parameters

| Parameter | Env var | Meaning |
|---|---|---|
| `mode` | `DEPLOY_MODE` | `full`, `provision_only` or `deploy_only` |
| `cellKeys` | `CELL_KEYS` | JSON array of keys from the catalog, e.g. `["cell_1_westus"]` |
| `appVersion` | `APP_VERSION` | Image tag and `BUILD_NUMBER` |
| `deployerPrincipalId` | `DEPLOYER_PRINCIPAL_ID` | Object id of the deploying identity |
| `deployerPrincipalType` | `DEPLOYER_PRINCIPAL_TYPE` | `ServicePrincipal` (default, pipeline) or `User` (local run with `az login`) |
| `grafanaEnabled` | `GRAFANA_ENABLED` | Creates Azure Managed Grafana with the cell dashboard (default `true`, incurs Azure charges; set `false` to opt out) |
| `grafanaSku` | `GRAFANA_SKU` | `Standard` (default) or `Essential` |

## Modes

| Resources | `full` | `provision_only` | `deploy_only` |
|---|:-:|:-:|:-:|
| Global (Front Door, WAF) | yes | yes | |
| Cell resources | yes | yes | |
| Cell apps | yes | | yes |

## Which release mode do I run?

Run the **Official Release Pipeline** manually and pick `deploy_mode`:

| Situation | `deploy_mode` | What happens |
|---|---|---|
| First deployment, or the infra changed (Bicep, AppHost topology, SKUs, new cell) | `full` | In one run: (1) Bicep `provision_only` creates/updates registry, cells, Front Door; (2) images are pushed to the registry (existing tags are skipped); (3) Bicep `deploy_only` creates/updates the Entra apps (if `PROVISION_AUTH_APP=true`) and rolls out the new app version in every selected cell. |
| Normal release of a new app version | `deploy_only` | Steps 2 and 3 only. Faster, and it cannot change infra. Fails with a clear message if the registry does not exist yet. |
| Only publish the GitHub release | `none` | No Azure access. |

Notes:
- There is no need to run the pipeline twice the first time: `full` chains the steps. They are separate internally only because the registry must exist before images can be pushed.
- Running `full` again is safe; Bicep deployments are incremental and the Entra apps are matched by `uniqueName`.
- To add a cell: add it to the catalog in `main.bicep`, pass its key in `cell_keys`, run `full`.
- `provision_only` exists in Bicep (`DEPLOY_MODE`) but is not a pipeline option.

## Image registration and rollout

An image version is registered **once** in the global registry (skipped if the tag already exists) and then enabled **per cell**: `cell_apps.bicep` points the container apps at `<registry>/<image>:<appVersion>`, which creates a new ACA revision; ACA pulls with the environment's managed identity (AcrPull, granted by the Aspire-generated cell resources) and shifts traffic once the revision is healthy.
Rolling a version out to more cells later is a `deploy_only` run with those `cell_keys`.

The registry must exist before images can be pushed, so the release pipeline runs `full` as `provision_only` -> push images (once) -> `deploy_only`.
Running `full` directly works only when the images for `appVersion` are already in the registry.

## Cells

The catalog in `main.bicep` maps a key to a cell id and an Azure region. A cell is the resource group `dst-<cellId>-<region>-rg` and one Orleans cluster (`dst-<cellId>`); cells never talk to each other.
Keys follow `cell_<cellId>_<region>` (the release pipeline derives the resource group from the key), e.g. `cell_1_westus`, `cell_2_eastus`, `cell_3_northeurope`.
To add a cell, add an entry to `cellCatalog` and pass its key in `cellKeys`.

## Naming convention

Cell resources use `<app>-<cellId>-<type>` (or `<app>-<cellId>-<service>-<type>`); global resources omit the cell id. Every name ends in a short type suffix (Azure CAF abbreviations), lowercase, hyphen separated. Cell ids must be unique in the catalog and use 1-9 lowercase alphanumeric characters, so even the silo container app fits Azure's 32-character limit. A hyphen is used instead of an underscore because underscores are rejected by most Azure names (storage, registry, Front Door, Container Apps, Grafana, WAF).

| Resource | Name | Azure limits respected |
|---|---|---|
| Resource groups | `dst-global-rg`, `dst-<cellId>-<region>-rg` | 1-90 chars |
| Container registry | `dst<hash>acr` | 5-50, alphanumeric only, global |
| Storage account | `dst<hash>st` | 3-24, lowercase alphanumeric only, global |
| Front Door profile / endpoint / origin group / route / security policy | `dst-afd`, `dst-api-ep`, `dst-web-api-og`, `dst-default-route`, `dst-waf-sp` | letters, digits, hyphens |
| Origins | `cell-<id>-<region>-origin` | no underscores |
| WAF policy | `dstwaf` | letters and digits only, must start with a letter |
| Managed Grafana | `dst-<hash8>-amg` | 2-23, global |
| Container Apps environment | `dst-<cellId>-cae` | max 32, lowercase alphanumeric and hyphens |
| Container apps | `dst-<cellId>-web-api-ca`, `dst-<cellId>-web-orleans-silo-ca` | max 32, lowercase, no `--` |
| Managed identities | `dst-<cellId>-web-api-id`, `dst-<cellId>-web-orleans-silo-id`, `dst-grafana-import-id` (global) | |
| Log Analytics / App Insights / VNet | `dst-<cellId>-log`, `dst-<cellId>-appi`, `dst-<cellId>-vnet` | |
| Private endpoints / NICs | `dst-<cellId>-clustering-pep`, `dst-<cellId>-grainstate-pep` (`-nic`) | |
| Container Apps infrastructure group | `<cell resource group>-cae-infra-rg` | created by Azure (platform load balancer + public IP) |

`<hash>` is `uniqueString(...)`, used only where Azure requires a globally unique name. Aspire-generated names are set in `AppHost.cs` (`ConfigureInfrastructure`) and mirrored in `cell_apps.bicep`. Still Aspire-chosen: the ACA environment's registry identity (`aca_mi-<hash>`), the private DNS zones (`privatelink.*`, mandatory names) and their `Dst-vnet-link` children.

The generated bundle remains cell-agnostic: `cell_resources.bicep` and `cell_apps.bicep` pass the same cell id into the generated modules. **Migration:** cell-prefixed names create new Azure resources; they do not rename an existing deployment. For an already deployed cell, plan an infrastructure recreation/migration, preserve any needed storage data, and remove superseded resources only after the new cell is healthy. Incremental deployments do not delete the old resources.

## Network and edge security

Generated by Aspire (published mode only):
- one VNet per cell; the ACA environment is injected into a delegated subnet;
- the storage account has public network access **disabled** and is reachable only through private endpoints (blob and table) with private DNS zones;
- apps reach storage and Application Insights with their managed identities only (no keys).

Public traffic: Internet -> Front Door (WAF) -> Web API (ACA external ingress). The Web API receives the Front Door id (`FrontDoor__Id`, looked up in `cell_apps.bicep`) and returns 403 to any request without the matching `X-Azure-FDID` header. The silo has internal ingress only.

Synthetic weather traffic calls the same Web API replica over loopback, with a per-process credential restricted to `POST /api/commands/weather/update`. Only these validated self-calls can skip the Front Door check and satisfy weather-update authentication without an Entra token. Public authentication and feature-management authorization are unchanged. See [traffic generation](../../../../docs/observability.md#synthetic-http-traffic).

## Pipelines

- **Official build** (push to `main`): tags the version and produces artifacts only (`build-artifacts`, `deploy-artifacts` with `infra/`, `deploy/` and container image archives). It never touches Azure.
- **Official release** (manual): inputs `deploy_mode` (`none`, `full`, `deploy_only`) and `cell_keys`.

Environment `azure` needs the variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (OIDC federated credential; Owner, or Contributor + User Access Administrator, on the subscription).

## Run it yourself, step by step (PowerShell)

Everything below runs from your machine with your own Azure login. It deploys to real Azure and costs money
The defaults are the cheap SKUs (Front Door Standard, ACR Basic), see "Cost and SKUs" below. Delete the resource groups afterwards.
Prerequisites: .NET SDK, Docker, Aspire CLI, Azure CLI **2.61 or newer** with Bicep (`az upgrade`, then `az bicep upgrade`; check with `az version`). Older CLIs do not understand `.bicepparam` files and fail with `incorrect usage: Chose only one of --template-file FILE | --template-uri URI`. You need Contributor + Role Based
Access Control Administrator (or Owner) on the subscription.

### 1. Generate the Bicep (offline)

```powershell
cd src\Aspires\Dst.Aspires.AppHost
foreach ($n in 'cellId','buildNumber','frontDoorId','registryName','registryResourceGroup','authMode','authAuthority','authAudience','authScalarClientId','authScalarScope','docsEnabled') { Set-Item "env:Parameters__$n" 'placeholder' }
aspire publish -o ./infra -c Release
```

The placeholders only satisfy the CLI; real values are supplied at deployment time. Result: `infra\` (generated, git-ignored).

### 2. Compile and dry-run

```powershell
$env:APP_VERSION = '0.0.1-local'     # also the image tag
$env:CELL_KEYS = '["cell_1_westus"]'
az bicep build --file deploy\main.bicep --stdout > $null
az login                              # once
$env:DEPLOYER_PRINCIPAL_ID = az ad signed-in-user show --query id -o tsv
$env:DEPLOYER_PRINCIPAL_TYPE = 'User'   # you are a user, not a service principal
$env:DEPLOY_MODE = 'provision_only'
az deployment sub what-if --location westeurope --parameters deploy\main.bicepparam
```

### 3. Provision resources (global registry, cells, Front Door)

```powershell
$env:DEPLOY_MODE = 'provision_only'
az deployment sub create --name dst-provision-local --location westeurope --parameters deploy\main.bicepparam
```

### 4. Build and push the images (once per version)

```powershell
cd ..\..\..   # repo root
dotnet publish src\Dst.WebApiApp\Dst.WebApiApp.csproj -c Release -t:PublishContainer -r linux-x64 --self-contained false -p:ContainerRepository=dst-web-api -p:ContainerImageTags=$env:APP_VERSION
dotnet publish src\OrleansSilo\Dst.OrleansSilo.WebApp\Dst.OrleansSilo.WebApp.csproj -c Release -t:PublishContainer -r linux-x64 --self-contained false -p:ContainerRepository=dst-web-orleans-silo -p:ContainerImageTags=$env:APP_VERSION

$acr = az acr list -g dst-global-rg --query "[0].name" -o tsv
$server = az acr show -n $acr --query loginServer -o tsv
az acr login --name $acr
foreach ($i in 'dst-web-api','dst-web-orleans-silo') {
  docker tag "${i}:$env:APP_VERSION" "$server/${i}:$env:APP_VERSION"
  docker push "$server/${i}:$env:APP_VERSION"
}
```

`PublishContainer` loads the image into local Docker. Role assignments can take a minute: retry `az acr login` / `docker push` on 401/403.

If `az acr login` fails on Windows with `error storing credentials ... Not enough memory resources are available` (sometimes reported as the password being too long), the Windows credential store (Docker Desktop's `desktop`/`wincred` helper) cannot hold the large Entra token. Use one of these workarounds; the apps pull with managed identity, so either is only for the local push.

**Option A (recommended, no admin user):** in the same PowerShell window, point Docker at a temporary config and hide the credential helpers from `PATH` for this window only, so the token is kept in a temporary file instead. Docker otherwise falls back to `wincred` even with an empty config. This replaces `az acr login` and the push loop above (`az acr login` itself runs `docker` from `PATH`, so the token is passed to `docker login` directly):

```powershell
$docker = (Get-Command docker).Source
$env:DOCKER_CONFIG = Join-Path $env:TEMP "dst-docker-$(New-Guid)"
New-Item -ItemType Directory $env:DOCKER_CONFIG | Out-Null
Set-Content (Join-Path $env:DOCKER_CONFIG 'config.json') '{}' -Encoding ascii
$helperDirs = Get-Command docker-credential-* -ErrorAction SilentlyContinue | ForEach-Object { Split-Path $_.Source } | Sort-Object -Unique
$env:PATH = ($env:PATH -split ';' | Where-Object { $_ -and ($helperDirs -notcontains $_.TrimEnd('\')) }) -join ';'

az acr login --name $acr --expose-token --query accessToken -o tsv |
  & $docker login $server --username 00000000-0000-0000-0000-000000000000 --password-stdin
foreach ($i in 'dst-web-api','dst-web-orleans-silo') {
  & $docker tag "${i}:$env:APP_VERSION" "$server/${i}:$env:APP_VERSION"
  & $docker push "$server/${i}:$env:APP_VERSION"
}

Remove-Item -Recurse -Force $env:DOCKER_CONFIG   # deletes the stored token
Remove-Item Env:DOCKER_CONFIG
```

Docker warns that credentials are stored unencrypted. The file holds a short-lived ACR token and is deleted at the end. `PATH` changes only affect this window; open a new window afterwards if you need the helpers again.

**Option B:** temporarily enable the registry admin user (short password), then disable it again:

```powershell
az acr update --name $acr --admin-enabled true
$pw = az acr credential show --name $acr --query "passwords[0].value" -o tsv
docker login "$acr.azurecr.io" --username $acr --password $pw
# ... docker push ...
az acr update --name $acr --admin-enabled false
```

### 5. Deploy the apps

```powershell
cd src\Aspires\Dst.Aspires.AppHost
$env:DEPLOY_MODE = 'deploy_only'
# optional: $env:PROVISION_AUTH_APP = 'true'   (needs the Graph permission, see below)
az deployment sub create --name dst-apps-local --location westeurope --parameters deploy\main.bicepparam
```

### 6. Check what worked

```powershell
az deployment sub list --query "[?starts_with(name,'dst-')].{n:name,s:properties.provisioningState}" -o table
az deployment operation sub list --name dst-apps-local --query "[?properties.provisioningState=='Failed'].properties.statusMessage" -o json   # why it failed
az deployment group list -g dst-1-westus-rg -o table                                                                                          # nested modules
az containerapp list -g dst-1-westus-rg --query "[].{n:name,latest:properties.latestRevisionName,ready:properties.latestReadyRevisionName}" -o table
az containerapp logs show -g dst-1-westus-rg -n dst-1-web-api-ca --follow
$fd = az afd endpoint list -g dst-global-rg --profile-name dst-afd --query "[0].hostName" -o tsv
curl "https://$fd/health"
```

Front Door can take a few minutes to start routing; its first health probe fails until the apps are up.

### Cost and SKUs (defaults are cheap; switch to Premium for production)

By default a deployment uses Basic ACR, Standard Front Door and Standard Managed Grafana. Managed Grafana is enabled by default and adds recurring charges; opt out with `GRAFANA_ENABLED=false` before provisioning if it is not needed.
For production switch to Premium by setting these before step 3 (locally as environment variables, in the pipeline as
variables of the GitHub environment `azure`):

```powershell
$env:REGISTRY_SKU = 'Premium'                        # ACR: geo-replication to every cell region (~ $50/month vs Basic ~ $5)
$env:FRONT_DOOR_SKU = 'Premium_AzureFrontDoor'       # Front Door: managed WAF rules (~ $330/month vs Standard ~ $35)
```

Switch by re-running with `deploy_mode=full`. Azure may not allow changing the SKU of an existing Front Door profile in
place; if the deployment rejects it, delete the Front Door profile (resource group `dst-global-rg`) and run `full` again.
Premium registry replicas are created in the cell regions automatically.

| Resource | Default (test) | Production | Notes |
|---|---|---|---|
| Front Door | Standard | Premium | Premium is needed only for managed WAF rules (OWASP, bots) and Private Link origins. The `X-Azure-FDID` check and rate limiting work on both. |
| Container registry | Basic | Premium | Premium only matters for geo-replication across several cells. |
| Private endpoints (storage tables + blobs) | as is | as is | ~ $15/month together, charged per hour. |
| Container Apps | as is | as is | Consumption plan; 1 replica each. Pay per use, small for a test. |
| Log Analytics / App Insights | as is | as is | Pay per GB; negligible at test volume. |
| Managed Grafana | Standard, enabled | Standard | Recurring charges; opt out with `GRAFANA_ENABLED=false`, or use Essential where available. |

Costs accrue hourly, so a short test is a few dollars. Delete the resource groups when done (see cleanup below). Front Door profiles are the thing to not forget.

### Dashboards in Azure (Managed Grafana enabled by default)

Locally the dashboard runs on Prometheus + Grafana containers. In Azure the metrics land in Application Insights / Log Analytics (table `AppMetrics`), so the Azure dashboard is a separate KQL version of it: `grafana/cell-overview.azure.json`.

```powershell
$env:GRAFANA_ENABLED = 'false'    # optional opt-out before step 3 (provision)
# Keep the default true to provision Grafana; GRAFANA_SKU = 'Essential' is the cheaper tier.
```

- Creates one Managed Grafana in `dst-global-rg`, gives it read access (Monitoring Reader, Log Analytics Reader) on every cell resource group, makes the deployer a Grafana admin, and imports the dashboard with a deployment script (dashboards are not ARM resources, they live inside Grafana).
- The dashboard has a **Cell** selector; each cell is its own Log Analytics workspace. The Grafana URL is the deployment output `grafanaEndpoint`.
- The script runs on every `full` / `provision_only` deployment, so dashboard edits roll out with the infrastructure. Dashboard changes made in the Grafana UI are overwritten.
- Differences from the local dashboard: metrics in Application Insights do not carry `app.cell.id` / `app.build.number` (the cell is the workspace; the build shows as the Container Apps revision, so there is no Build selector). The generator takes layout and presentation from the local dashboard, preserves existing Azure KQL queries for unchanged panels, and applies Azure-specific query mappings from `deploy/grafana/generate-azure-dashboard.py` where the source panel has changed. "Deactivated" is derived from created activations minus the change in active grains, as locally.
- After changing the local dashboard, add any panel whose metric query or dimensions changed to `AZURE_QUERY_OVERRIDES` and update its KQL mapping in `deploy/grafana/generate-azure-dashboard.py`. Then run `python deploy/grafana/generate-azure-dashboard.py observability/grafana/dashboards/cell-overview.json deploy/grafana/cell-overview.azure.json` and commit the generated output. The existing Azure JSON is required as the baseline to retain Azure-only query fixes for unchanged panels; the generator fails rather than silently rebuilding from stale fallback queries if that file is missing.
- The deployment script uses a temporary storage account and container; if a policy blocks shared-key storage access it will fail.
- Delete the instance with the `dst-global-rg` resource group.
### Redeploy and cleanup

- New app version: repeat steps 4 and 5 with a new `APP_VERSION`. Infra change: repeat step 3 first.
- Cleanup: `az group delete -n dst-global-rg --yes --no-wait` and `az group delete -n dst-1-westus-rg --yes --no-wait` (the Container Apps infrastructure group `dst-1-westus-rg-cae-infra-rg` is removed with the environment). Entra apps `dst-api` / `dst-scalar` (if created) must be deleted separately.

`cell_apps.bicep` mirrors Aspire's deterministic resource naming; re-run the build above after renaming AppHost resources.

## One-time manual setup (not covered by Bicep)

**Azure (for the release pipeline)**
1. Create an Entra app registration (or user-assigned identity) for GitHub and add a **federated credential** for the repo and the GitHub environment `azure` (subject `repo:<org>/<repo>:environment:azure`).
2. Give it, on the subscription, **Contributor** and **Role Based Access Control Administrator** (the Bicep creates role assignments: AcrPull, storage data roles, App Insights publisher). Alternatively Owner.
3. Register resource providers once: `Microsoft.App`, `Microsoft.ContainerRegistry`, `Microsoft.Cdn`, `Microsoft.Network`, `Microsoft.Storage`, `Microsoft.OperationalInsights`, `Microsoft.Insights`, `Microsoft.ManagedIdentity`, `Microsoft.AlertsManagement`, and for Grafana `Microsoft.Dashboard` and `Microsoft.ContainerInstance` (the dashboard import script runs in a container).
4. In GitHub create environment `azure` (add required reviewers if you want an approval gate) with variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. No secrets are needed (OIDC).

Commands (PowerShell, `az` logged in; `gh` is optional, otherwise use the GitHub web UI for step 4):
```powershell
$sub    = az account show --query id -o tsv
$tenant = az account show --query tenantId -o tsv
$repo   = '<org>/<repo>'

# 1. identity + federated credential (trusts only environment "azure")
$appId = az ad app create --display-name dst-github-deployer --query appId -o tsv
az ad sp create --id $appId
@{ name='github-azure-env'; issuer='https://token.actions.githubusercontent.com'
   subject="repo:${repo}:environment:azure"; audiences=@('api://AzureADTokenExchange') } |
  ConvertTo-Json -Compress | Out-File $env:TEMP\cred.json -Encoding ascii
az ad app federated-credential create --id $appId --parameters "@$env:TEMP\cred.json"

# 2. roles
az role assignment create --assignee $appId --role Contributor --scope "/subscriptions/$sub"
az role assignment create --assignee $appId --role "Role Based Access Control Administrator" --scope "/subscriptions/$sub"

# 3. providers
'Microsoft.App','Microsoft.ContainerRegistry','Microsoft.Cdn','Microsoft.Network','Microsoft.Storage',
'Microsoft.OperationalInsights','Microsoft.Insights','Microsoft.ManagedIdentity','Microsoft.AlertsManagement',
'Microsoft.Dashboard','Microsoft.ContainerInstance' | ForEach-Object { az provider register --namespace $_ }

# 4. GitHub environment + variables (needs the gh CLI and `gh auth login`)
gh api -X PUT repos/$repo/environments/azure
gh variable set AZURE_CLIENT_ID -e azure -R $repo -b $appId
gh variable set AZURE_TENANT_ID -e azure -R $repo -b $tenant
gh variable set AZURE_SUBSCRIPTION_ID -e azure -R $repo -b $sub
```

Also under repo Settings → Actions → General → Workflow permissions allow read and write (the build pipeline pushes `v*` tags), and make sure no tag protection rule blocks `v*`.

Run order: merge to `main` (Official Build Pipeline), then run Official Release Pipeline with `deploy_mode=full` and `cell_keys=cell_1_westus`; later versions use `deploy_only`. If a role assignment fails with "Authorization failed", step 2 is missing; if login fails, the federated credential subject does not match the repo/environment name.
**Web API authentication (optional, default is anonymous)**

*Automatic (recommended):* set the GitHub environment variable `PROVISION_AUTH_APP=true`. `auth_app.bicep` then creates the API app (scope `access_as_user`) and the Scalar SPA app (redirect URI = Front Door host) in Entra ID via the Microsoft Graph Bicep extension (`bicepconfig.json`), and feeds authority/audience/client id/scope to the Web API. Everything is global and shared by all cells. One-time requirement: the deploying identity needs the Microsoft Graph application permission `Application.ReadWrite.OwnedBy` with admin consent. None of these values are secrets (JWT validation and SPA + PKCE need no client secret), so no Key Vault is used; add one when a real secret appears.

*Manual (e.g. another identity provider):* leave `PROVISION_AUTH_APP` unset and do the following.
1. Entra app registration "dst-api": set an Application ID URI (`api://<id>`) and expose a scope (e.g. `access_as_user`).
2. For the Scalar login button, a second registration of type *Single-page application* with redirect URI `https://<front-door-host>/scalar/` (and the API scope granted).
3. Set these variables on the GitHub environment `azure`:

| Variable | Example |
|---|---|
| `AUTH_MODE` | `JwtBearer` (empty/`None` = anonymous) |
| `AUTH_AUTHORITY` | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| `AUTH_AUDIENCE` | `api://<api-app-id>` |
| `AUTH_SCALAR_CLIENT_ID` | client id of the SPA registration (empty = no login button) |
| `AUTH_SCALAR_SCOPE` | `api://<api-app-id>/access_as_user` |
| `DOCS_ENABLED` | `true` / `false` |

These become the Bicep params `authMode`, `authAuthority`, ... (see `main.bicepparam`) and are applied only when apps are deployed.

**Later, not automated:** custom domain + certificate on Front Door, alert rules, and budgets.