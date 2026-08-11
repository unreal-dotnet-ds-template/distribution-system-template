# Aspire + Orleans Distribution System Template

A production-ready `dotnet new` template for building distributed systems with **.NET Aspire** for cloud-native orchestration and **Microsoft Orleans** for virtual actor business logic.

> 💡 **The 3-Folder Rule:** You only touch three folders to build your application:
> - `src/Dst.Core` — Interfaces, grain contracts, shared DTOs
> - `src/Dst.Features` — Grain implementations (your business logic)
> - `src/Dst.WebApiApp` — HTTP endpoints & API routing
>
> Everything else (Silo hosting, Redis clustering, OpenTelemetry, health checks) is preconfigured infrastructure.

---

## ⚡ Quickstart

Get up and running in 30 seconds:

```bash
# 1. Install template
dotnet new install Dst.AspireOrleans.Template

# 2. Create your project
dotnet new dst-aspire-orleans -n MyPaymentSystem -o ./my-payment-system
cd my-payment-system

# 3. Run everything (Aspire Dashboard + Silo + Web API + Redis)
dotnet run --project src/Aspires/Dst.Aspires.AppHost
```

Once running:
- Open the **Aspire Dashboard** (URL displayed in your terminal, e.g. `https://localhost:17228`) to inspect resources, console logs, and OpenTelemetry traces.
- Access the **Interactive API Reference (Scalar)** at `https://localhost:<port>/scalar/v1` to test endpoints.

---

## 🚀 Your First Feature in 5 Minutes (E2E Flow)

Let's build a distributed **BankAccount** feature (`Deposit` and `GetBalance`) to see how Orleans and Aspire work together.

### Step 1: Define the Contract in `Dst.Core`
Create `src/Dst.Core/Features/BankAccounts/IBankAccountGrain.cs`:

```csharp
namespace Dst.Core.Features.BankAccounts;

public interface IBankAccountGrain : IGrainWithStringKey
{
    Task<decimal> DepositAsync(decimal amount);
    Task<decimal> GetBalanceAsync();
}
```

### Step 2: Implement the Grain in `Dst.Features`
Create `src/Dst.Features/BankAccounts/BankAccountGrain.cs`:

```csharp
using Dst.Core.Features.BankAccounts;

namespace Dst.Features.BankAccounts;

public class BankAccountGrain : Grain, IBankAccountGrain
{
    private decimal _balance;

    public Task<decimal> DepositAsync(decimal amount)
    {
        _balance += amount;
        return Task.FromResult(_balance);
    }

    public Task<decimal> GetBalanceAsync() => Task.FromResult(_balance);
}
```

### Step 3: Expose HTTP Endpoints in `Dst.WebApiApp`
In `src/Dst.WebApiApp/Program.cs`, map endpoints using `IClusterClient`:

```csharp
app.MapPost("/accounts/{id}/deposit", async (
    [FromServices] IClusterClient client, 
    string id, 
    [FromBody] decimal amount) =>
{
    var account = client.GetGrain<IBankAccountGrain>(id);
    var newBalance = await account.DepositAsync(amount);
    return Results.Ok(new { AccountId = id, Balance = newBalance });
}).WithName("DepositToAccount");

app.MapGet("/accounts/{id}/balance", async (
    [FromServices] IClusterClient client, 
    string id) =>
{
    var account = client.GetGrain<IBankAccountGrain>(id);
    var balance = await account.GetBalanceAsync();
    return Results.Ok(new { AccountId = id, Balance = balance });
}).WithName("GetAccountBalance");
```

### Step 4: Run & Verify
Launch the solution:
```bash
dotnet run --project src/Aspires/Dst.Aspires.AppHost
```
1. Open the **Scalar API docs** (`/scalar/v1`) or use `curl`:
   ```bash
   # Deposit $500 into account "acc-101"
   curl -X POST https://localhost:<port>/accounts/acc-101/deposit -H "Content-Type: application/json" -d "500"

   # Check balance
   curl https://localhost:<port>/accounts/acc-101/balance
   # Returns: {"accountId":"acc-101","balance":500}
   ```
2. Open the **Aspire Dashboard** $\rightarrow$ **Traces** to see the full distributed call flow from HTTP API $\rightarrow$ Orleans Silo $\rightarrow$ Grain execution!

> [!TIP]
> **Notice what you did NOT do:** No DI registrations for grains, no database connection strings, no concurrency lock handling, no manual Docker Compose scripts. Orleans and Aspire handle it automatically.

---

## 💡 Why Orleans + Aspire?

Building distributed systems traditionally requires managing databases, cache syncs, concurrency locks, message brokers, and docker-compose configurations. This template solves those pain points out-of-the-box:

| Challenge | Traditional Microservices | With Orleans + Aspire (This Template) |
|---|---|---|
| **State & Concurrency** | Manual DB transactions, distributed locks, cache invalidation | **Virtual Actors (Orleans):** Single-threaded execution per grain, in-memory state, auto-activation/deactivation. |
| **Local Development** | Wrestling `docker-compose.yml`, port collisions, service start delays | **.NET Aspire Orchestration:** Redis, Silos, APIs, and OpenTelemetry boot with a single F5 / `dotnet run`. |
| **Project Clutter** | Dozens of boilerplate files, complex DI setup for every service | **The 3-Folder Rule:** Focus only on `Core`, `Features`, and `WebApiApp`. Infrastructure stays untouched. |
| **Testing** | Heavy test containers, flaky network mocking | **Native E2E & Functional Tests:** Full-solution integration testing via Aspire `DistributedApplicationTestingBuilder` and in-memory Orleans `TestCluster`. |
| **Maintenance** | Package version drift, inconsistent build flags | **Modern .NET Standards:** Central Package Management (CPM), pinned .NET 9 SDK, and automated MinVer semantic releases. |

---

## 📂 Project Structure & Architecture

```
/
├── src/
│   ├── Dst.Core/                        ← 🟢 [TOUCH THIS] Interfaces, grain contracts, models
│   │   └── Features/
│   │       ├── WeatherForecasts/        ← Sample grain interface
│   │       └── BankAccounts/            ← Your grain interfaces
│   │
│   ├── Dst.Features/                    ← 🟢 [TOUCH THIS] Grain implementations (business logic)
│   │   ├── WeatherForecasts/            ← Sample grain logic
│   │   └── BankAccounts/                ← Your grain logic
│   │
│   ├── Dst.WebApiApp/                   ← 🟢 [TOUCH THIS] HTTP API & client endpoints
│   │
│   ├── OrleansSilo/
│   │   └── Dst.OrleansSilo.WebApp/      ← ⚙️ [INFRA] Orleans Silo host (runs your grains)
│   │
│   └── Aspires/
│       ├── Dst.Aspires.AppHost/         ← ⚙️ [INFRA] Aspire orchestrator (wires services together)
│       └── Dst.Aspires.ServiceDefaults/ ← ⚙️ [INFRA] OpenTelemetry, health checks, resilience
│
├── tests/
│   └── Dst.HostApplication.Tests/      ← 🧪 Integration tests against the real AppHost
│
├── .github/workflows/                   ← CI/CD pipelines (PR build, Official build & release)
├── build/                              ← Shared MSBuild customizations
├── global.json                         ← Pinned .NET SDK version
├── Directory.Build.props               ← Centralized MSBuild properties & analyzer rules
├── Directory.Build.targets             ← Centralized MSBuild targets
├── Directory.Packages.props            ← Central NuGet package version management
├── NuGet.Config                        ← NuGet package source configuration
└── .editorconfig                       ← Code style rules
```

### System Architecture Flow

```
                     ┌────────────────────────┐
                     │     HTTP Request       │
                     └───────────┬────────────┘
                                 │
                                 ▼
                     ┌────────────────────────┐
                     │     Dst.WebApiApp      │ (Orleans Client + Scalar UI)
                     └───────────┬────────────┘
                                 │ IClusterClient.GetGrain<T>()
                                 ▼
                     ┌────────────────────────┐
                     │ Dst.OrleansSilo.WebApp │ (Hosts Dst.Features)
                     └───────────┬────────────┘
                                 │
               ┌─────────────────┴─────────────────┐
               ▼                                   ▼
    ┌──────────────────────┐            ┌──────────────────────┐
    │ Redis (Clustering)   │            │ Redis (Grain Storage)│
    └──────────────────────┘            └──────────────────────┘
               ▲                                   ▲
               └─────────────────┬─────────────────┘
                                 │ Managed by
                     ┌───────────┴────────────┐
                     │   Dst.Aspires.AppHost  │ (Orchestrator + Dashboard)
                     └───────────┴────────────┘
```

### 🏛️ CQRS & Read-Side Architecture (Best Practices)

When building distributed systems with Orleans, follow the **Command Query Responsibility Segregation (CQRS)** pattern:

```
                               ┌───────────────────────────┐
                               │  🌐 Client / UI / Frontend │
                               └─────────────┬─────────────┘
                                             │
                      ┌──────────────────────┴──────────────────────┐
                      │                                             │
             [Commands: POST/PUT/DELETE]                  [Queries: GET Lists/Search]
                      │                                             │
                      ▼                                             ▼
       ┌──────────────────────────────┐              ┌──────────────────────────────┐
       │   Dst.WebApiApp (Commands)   │              │    Dst.WebApiApp (Queries)   │
       └──────────────┬───────────────┘              └──────────────┬───────────────┘
                      │                                             │
                      │ IClusterClient.GetGrain<T>(id)              │ Direct Read Query
                      ▼                                             │ (Bypasses Orleans)
       ┌──────────────────────────────┐                             │
       │    Dst.OrleansSilo.WebApp    │                             │
       │ 🌾 Grain (Aggregate Root)    │                             │
       │    - Single-threaded logic   │                             │
       │    - In-memory state mutation│                             │
       └──────┬───────────────────────┘                             │
              │                                                     │
     ┌────────┴────────┬─────────────────────────┐                  │
     │                 │ State write             │ Projection /     │
     ▼                 ▼                         │ Sync Events      │
┌─────────┐   ┌──────────────────┐               ▼                  │
│  Redis  │   │  Grain Storage   │    ┌──────────────────────┐      │
│ Cluster │   │ (Redis/Postgres) │    │  📊 Read Database    │◄─────┘
└─────────┘   └──────────────────┘    │ (Postgres/Mongo/ES)  │
                                      └──────────────────────┘
```

#### The Role of Orleans: Write-Side / Command Engine
Orleans grains act as **Aggregate Roots** in Domain-Driven Design (DDD). They provide:
- **Single-threaded execution** per grain (no distributed locks or concurrency race conditions).
- **In-memory state caching** with automatic activation and lifecycle management.
- **Strong transactional boundaries** for state mutations and business invariants addressed by a unique ID.

#### The Anti-Pattern: Querying Lists Across Grains
> [!WARNING]
> **Do not design list, search, aggregation, or paginated queries across Orleans grains.**
> 
> Looping over IDs to invoke hundreds of grains or building "Index/Registry" grains causes:
> - **Massive memory bloat** by needlessly activating inactive grains into memory.
> - **High network latency** due to multiple inter-silo RPC hops.
> - **Silo overload and garbage collection pressure**.

#### The CQRS Pattern Split

| Operation Type | HTTP Method | Data Flow | Responsibility |
|---|---|---|---|
| **Commands** | `POST`, `PUT`, `DELETE` | `API` $\rightarrow$ `IClusterClient` $\rightarrow$ `Orleans Grain` $\rightarrow$ State Store & Read Projection | State mutations, business rules, consistency. |
| **Point Reads** | `GET /items/{id}` | `API` $\rightarrow$ `IClusterClient` $\rightarrow$ `Orleans Grain` (or Read DB) | Fast single-entity reads where strong in-memory state is required. |
| **List & Search Queries** | `GET /items?filter=...` | `API` $\rightarrow$ `Read Database` (Bypasses Orleans entirely) | Paginated lists, complex joins, full-text search, reporting. |

---

## 🧪 Testing Strategy

The solution includes integration tests using Aspire's test hosting library (`Aspire.Hosting.Testing`), allowing you to test the full distributed application without external test infrastructure:

```csharp
// tests/Dst.HostApplication.Tests/WebTests.cs
[Fact]
public async Task GetWeatherForecast_ReturnsOkStatusCode()
{
    var cancellationToken = TestContext.Current.CancellationToken;
    var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Dst_Aspires_AppHost>(cancellationToken);

    await using var app = await appHost.BuildAsync(cancellationToken);
    await app.StartAsync(cancellationToken);

    using var httpClient = app.CreateHttpClient("web-api");
    await app.ResourceNotifications.WaitForResourceHealthyAsync("web-api", cancellationToken);

    var response = await httpClient.GetAsync("/weatherforecast", cancellationToken);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

Run all tests from the CLI:
```bash
dotnet test
```

---

## ⚙️ Key Conventions & Repository Operations

### 1. Central Package Management (CPM)
All NuGet package versions live exclusively in `Directory.Packages.props`. Project files (`.csproj`) reference packages **without** a `Version` attribute:

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Serilog" Version="4.0.0" />

<!-- Any .csproj file -->
<PackageReference Include="Serilog" />
```

To add a new package:
1. Add `<PackageVersion Include="Package.Name" Version="x.y.z" />` in `Directory.Packages.props`.
2. Add `<PackageReference Include="Package.Name" />` in your target `.csproj`.

### 2. Pinned .NET SDK
The .NET SDK version is pinned in `global.json`. Any update here applies instantly to all developers and CI/CD pipelines:
```json
{ "sdk": { "version": "9.0.300", "rollForward": "latestPatch" } }
```

### 3. ServiceDefaults
Every service references `Dst.Aspires.ServiceDefaults` and calls:
- `builder.AddServiceDefaults()` — configures OpenTelemetry (traces, metrics), service discovery, and standard resilience.
- `app.MapDefaultEndpoints()` — exposes `/health` and `/alive` endpoints.

### 4. Automatic Versioning (MinVer + Conventional Commits)
Versioning is handled automatically based on git history and commit messages:

| Commit Pattern | Version Bump | Example |
|---|---|---|
| `BREAKING CHANGE:`, `feat!:` | **Major** | `1.2.3` $\rightarrow$ `2.0.0` |
| `feat:` | **Minor** | `1.2.3` $\rightarrow$ `1.3.0` |
| `fix:`, `chore:`, `docs:` | **Patch** | `1.2.3` $\rightarrow$ `1.2.4` |

### 5. Cloud-Native Networking & TLS (Why No `UseHttpsRedirection`)
In `Dst.WebApiApp` and `Dst.OrleansSilo.WebApp`, `app.UseHttpsRedirection()` is intentionally **omitted**:
- **Edge TLS Termination:** In cloud-native deployments (Kubernetes Ingress, Azure Container Apps, AWS ALB, Nginx, Cloudflare), SSL/TLS termination and public HTTP $\rightarrow$ HTTPS redirection happen at the **Reverse Proxy / Ingress Controller** level. The ingress forwards decrypted traffic over internal HTTP to containers.
- **Prevents Infinite Redirect Loops:** Enforcing HTTPS redirection inside containers behind reverse proxies can cause infinite 307/308 redirect loops.
- **Reliable Internal Health Probes:** Container health checks (`/health`, `/alive`) and Aspire orchestrator probes communicate over internal HTTP. Removing redirection ensures probes receive immediate `200 OK` status codes without TLS certificate overhead or redirection errors.

---

## 🚢 CI/CD Pipelines & Repository Setup

The template includes ready-to-run GitHub Actions workflows in `.github/workflows/`:

| Pipeline | Trigger | Purpose |
|---|---|---|
| **PR Build** (`pr_validation_pipeline.yml`) | Pull Request $\rightarrow$ `main` | Validates build, analyzers, and tests as a PR gate. |
| **Official Build** (`build_and_publish_pipeline.yml`) | Push to `main` | Builds, runs tests, publishes artifacts, and creates git version tags. |
| **Official Release** (`release_pipeline.yml`) | Manual dispatch | Creates GitHub release from published build artifacts. |

### Repository Protection Setup (Run Once)
To enforce PR reviews, linear git history, and CI checks on your repository, run the setup script:

```bash
# Requires GitHub CLI (gh auth login)
bash scripts/setup-repo.sh
```

---

## 🏭 Production Readiness Checklist

When you are ready to transition from local development to production:

- [ ] **State Persistence:** Swap the development Redis grain storage (`AddRedis`) with your production database provider (e.g. Azure Table Storage, PostgreSQL, CosmosDB, or AWS DynamoDB via `builder.AddAzureTableClient(...)` or standard Orleans storage packages).
- [ ] **Clustering Provider:** Configure production Orleans clustering for your cloud platform (e.g., Azure Blob/Table, AWS DynamoDB, Kubernetes, or Redis Cluster).
- [ ] **Telemetry & Monitoring:** Route OpenTelemetry export (`OTEL_EXPORTER_OTLP_ENDPOINT`) to your monitoring platform (e.g., Azure Application Insights, Prometheus/Grafana, Datadog).
- [ ] **Secrets Management:** Use Azure Key Vault, AWS Secrets Manager, or Kubernetes Secrets instead of local `appsettings.Development.json`.
- [ ] **Health Probes:** Configure your load balancer or orchestrator (Kubernetes/ACA) to probe `/health` and `/alive`.

---

## 📄 License
Licensed under the [MIT License](LICENSE).
