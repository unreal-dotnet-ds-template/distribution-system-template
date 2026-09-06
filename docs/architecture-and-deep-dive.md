# 🏛️ Architecture & Technical Deep Dive

This document provides a deep dive into the architecture, design principles, conventions, and patterns used in this template.

---

## 💡 Why Orleans + Aspire?

Building distributed systems traditionally requires managing databases, cache synchronizations, concurrency locks, message brokers, and `docker-compose` configurations. This template solves those pain points out-of-the-box:

| Challenge | Traditional Microservices | With Orleans + Aspire (This Template) |
|---|---|---|
| **State & Concurrency** | Manual DB transactions, distributed locks, cache invalidation | **Virtual Actors (Orleans):** Single-threaded execution per grain, in-memory state, auto-activation/deactivation. |
| **Local Development** | Wrestling `docker-compose.yml`, port collisions, service start delays | **.NET Aspire Orchestration:** Redis, Silos, APIs, and OpenTelemetry boot with a single `dotnet run`. |
| **Project Clutter** | Dozens of boilerplate files, complex DI setup for every service | **The 3-Folder Rule:** Focus only on `Core`, `Features`, and `WebApiApp`. Infrastructure stays untouched. |
| **Testing** | Heavy test containers, flaky network mocking | **Native Integration Tests:** Full-solution integration testing via Aspire `DistributedApplicationTestingBuilder` and in-memory Orleans `TestCluster`. |
| **Maintenance** | Package version drift, inconsistent build flags | **Modern .NET Standards:** Central Package Management (CPM), pinned .NET 9 SDK, and automated MinVer semantic releases. |

---

## ⚙️ System Architecture Flow

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
                       │ Dst.OrleansSilo.WebApp │ (Hosts Dst.Features + Orleans Dashboard)
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

---

## 🏛️ CQRS & Read-Side Architecture (Best Practices)

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

### The Role of Orleans: Write-Side / Command Engine
Orleans grains act as **Aggregate Roots** in Domain-Driven Design (DDD). They provide:
- **Single-threaded execution** per grain (no distributed locks or concurrency race conditions).
- **In-memory state caching** with automatic activation and lifecycle management.
- **Strong transactional boundaries** for state mutations and business invariants addressed by a unique ID.

### The Anti-Pattern: Querying Lists Across Grains
> [!WARNING]
> **Do not design list, search, aggregation, or paginated queries across Orleans grains.**
> 
> Looping over IDs to invoke hundreds of grains or building "Index/Registry" grains causes:
> - **Massive memory bloat** by needlessly activating inactive grains into memory.
> - **High network latency** due to multiple inter-silo RPC hops.
> - **Silo overload and garbage collection pressure**.

### The CQRS Pattern Split

| Operation Type | HTTP Method | Data Flow | Responsibility |
|---|---|---|---|
| **Commands** | `POST`, `PUT`, `DELETE` | `API` $\rightarrow$ `IClusterClient` $\rightarrow$ `Orleans Grain` $\rightarrow$ State Store & Read Projection | State mutations, business rules, consistency. |
| **Point Reads** | `GET /items/{id}` | `API` $\rightarrow$ `IClusterClient` $\rightarrow$ `Orleans Grain` (or Read DB) | Fast single-entity reads where strong in-memory state is required. |
| **List & Search Queries** | `GET /items?filter=...` | `API` $\rightarrow$ `Read Database` (Bypasses Orleans entirely) | Paginated lists, complex joins, full-text search, reporting. |

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

### 3. ServiceDefaults Integration
Every service references `Dst.Aspires.ServiceDefaults` and invokes standard extension methods in `Program.cs`:
- `builder.AddServiceDefaults()` — Configures OpenTelemetry (traces, metrics), service discovery, and standard HTTP resilience policies.
- `app.MapDefaultEndpoints()` — Exposes `/health` and `/alive` endpoints.

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
