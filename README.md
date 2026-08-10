# Aspire + Orleans Distribution System Template

A production-ready `dotnet new` template for building distributed systems with **.NET Aspire** for orchestration and **Microsoft Orleans** for actor-based business logic.

> **You only need to touch two folders to build your application:**
> - `src/Dst.Core` — interfaces, contracts, shared models
> - `src/Dst.Features` — grain implementations (your business logic)
> - `src/Apps/Dst.Apps.OrleansClientWebApp` — Endpoints
>
> Everything else is infrastructure and can stay untouched.

---

## Getting Started

```bash
# Install the template (once)
dotnet new install Dst.AspireOrleans.Template

# Create your project
dotnet new dst-aspire-orleans -n MyCompany -o ./my-app
cd my-app

# Run everything with a single command
dotnet run --project src/Aspires/MyCompany.Aspires.AppHost
```

Aspire starts Redis, the Orleans Silo, the Orleans Client API, and the Web UI — all wired together automatically.

---

## Project Structure

```
/
├── src/
│   ├── Dst.Core/                        ← YOUR INTERFACES & MODELS (touch this)
│   │   └── Features/
│   │       └── WeatherForecasts/        ← Example: grain interface + DTOs
│   │
│   ├── Dst.Features/                    ← YOUR BUSINESS LOGIC (touch this)
│   │   └── WeatherForecasts/            ← Example: grain implementation
│   │
│   ├── Apps/
│   │   ├── Dst.Apps.OrleansSiloWebApp/  ← Orleans Silo — runs your grains
│   │   ├── Dst.Apps.OrleansClientWebApp/← Orleans Client API — HTTP endpoints calling grains
│   │   └── Dst.Apps.Web/               ← Blazor frontend — calls the Client API
│   │
│   └── Aspires/
│       ├── Dst.Aspires.AppHost/         ← Aspire orchestrator — wires everything together
│       └── Dst.Aspires.ServiceDefaults/ ← Shared: OpenTelemetry, health checks, resilience
│
├── tests/
│   └── Dst.HostApplication.Tests/      ← Integration tests against the real AppHost
│
├── .github/workflows/                   ← CI/CD pipelines
├── build/                              ← Shared MSBuild customizations
├── global.json                         ← .NET SDK version pin
├── Directory.Build.props               ← MSBuild properties for all projects
├── Directory.Build.targets             ← MSBuild targets for all projects
├── Directory.Packages.props            ← Central NuGet version management
├── NuGet.Config                        ← NuGet feed configuration
└── .editorconfig                       ← Code style rules
```

---

## How It Works

```
 [Blazor Web UI]
       │ HTTP (service discovery: "orleansClient")
       ▼
 [Orleans Client WebApp]   ← HTTP API, routes requests to grains
       │ Orleans grain calls
       ▼
 [Orleans Silo WebApp]     ← Hosts your grain implementations (Dst.Features)
       │ clustering + grain storage
       ▼
    [Redis]                ← Managed automatically by Aspire
```

**Aspire** handles service discovery, health checks, startup ordering, and local Redis provisioning.
**Orleans** handles distributed state, virtual actors, and grain lifecycle.

---

## Where to Add Your Code

### 1. Define your grain interface in `Dst.Core`

```csharp
// src/Dst.Core/Features/Orders/IOrderGrain.cs
namespace Dst.Core.Features.Orders;

public interface IOrderGrain : IGrainWithIntegerKey
{
    Task<OrderStatus> GetStatusAsync();
    Task PlaceAsync(OrderRequest request);
}
```

### 2. Implement the grain in `Dst.Features`

```csharp
// src/Dst.Features/Orders/OrderGrain.cs
namespace Dst.Features.Orders;

public class OrderGrain : Grain, IOrderGrain
{
    public Task<OrderStatus> GetStatusAsync() { /* ... */ }
    public Task PlaceAsync(OrderRequest request) { /* ... */ }
}
```

### 3. Expose it via an HTTP endpoint in `Dst.Apps.OrleansClientWebApp`

```csharp
app.MapPost("/orders", async ([FromServices] IClusterClient client, OrderRequest req) =>
{
    var grain = client.GetGrain<IOrderGrain>(req.Id);
    await grain.PlaceAsync(req);
    return Results.Created();
});
```

That's it. No service registration, no DI wiring for grains — Orleans discovers them automatically.

---

## Key Conventions

### Central Package Management
All NuGet versions live **only** in `Directory.Packages.props`. Project files reference packages **without** a version:

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Serilog" Version="4.0.0" />

<!-- YourProject.csproj -->
<PackageReference Include="Serilog" />
```

### .NET SDK Version
Pinned in `global.json` — one change propagates to all developers and all CI pipelines:
```json
{ "sdk": { "version": "9.0.300", "rollForward": "latestPatch" } }
```

### Versioning (Conventional Commits + MinVer)
Git tags are created **automatically by the Official Build Pipeline** on every merge to `main`. Never create tags manually.

| Commit pattern | Version bump |
|---|---|
| `BREAKING CHANGE`, `feat!:`, `fix!:` | **major** `1.2.3 → 2.0.0` |
| `feat:` | **minor** `1.2.3 → 1.3.0` |
| `fix:`, `chore:`, `docs:`, etc. | **patch** `1.2.3 → 1.2.4` |

### ServiceDefaults
Every service calls `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()`. This is what wires up:
- OpenTelemetry tracing and metrics
- Health check endpoints (`/health`, `/alive`)
- HTTP client resilience and service discovery

Do not remove these calls from the `Apps` projects.

---

## CI/CD Pipelines

| Pipeline | Trigger | What it does |
|---|---|---|
| **PR Build** | Pull request → `main` | Build + test gate |
| **Official Build** | Push to `main` / manual | Build + test + publish artifacts + create git tag |
| **Official Release** | Manual | Downloads artifacts, creates GitHub Release |

All pipelines use `fetch-depth: 0` so MinVer can read full git history for versioning.

### Repository Setup (run once after creating from template)

```bash
# Requires GitHub CLI: https://cli.github.com
bash scripts/setup-repo.sh
```

This sets branch protection on `main`:
- Blocks direct pushes (including admins)
- Requires PR with at least 1 approval
- Requires PR Build to pass
- Dismisses stale approvals on new commits

### Running a Release
1. Go to **Actions → Official Release Pipeline → Run workflow**
2. Optionally enter a **Build Run ID** (from Official Build history) — leave blank for latest

---

## Adding a NuGet Package

1. Add the version to `Directory.Packages.props`:
   ```xml
   <PackageVersion Include="Serilog" Version="4.0.0" />
   ```
2. Reference it in your `.csproj` **without** a version attribute:
   ```xml
   <PackageReference Include="Serilog" />
   ```

