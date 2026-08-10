# Copilot Instructions

## Build, Test & Lint Commands

```bash
# Restore, build, and test (Release configuration matches CI)
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release

# Run a single test class or method
dotnet test --no-build --configuration Release --filter "FullyQualifiedName~WebTests"

# Run tests with coverage
dotnet test --no-build --configuration Release --collect:"XPlat Code Coverage"
```

> **Note:** `TreatWarningsAsErrors=true` is set globally — all analyzer and style warnings are build errors.

## Architecture Overview

This is a **.NET Aspire** distributed application template. The key projects:

| Project | Role |
|---|---|
| `Dst.Aspires.AppHost` | Aspire orchestrator — defines the topology (which services run, their references, health checks) |
| `Dst.WebApiApp` | HTTP API — Orleans client, exposes endpoints that call grains |
| `Dst.OrleansSilo.WebApp` | Orleans Silo — hosts grain implementations from `Dst.Features` |
| `Dst.Core` | Contracts — grain interfaces, models, shared types |
| `Dst.Features` | Business logic — grain implementations |
| `Dst.Aspires.ServiceDefaults` | Shared library referenced by every service — wires up OpenTelemetry, health checks (`/health`, `/alive`), HTTP resilience, and service discovery |
| `Dst.HostApplication.Tests` | Integration tests using `DistributedApplicationTestingBuilder` — spins up the real AppHost |

**The AppHost is the single source of truth for the service graph.** Service-to-service HTTP calls use Aspire service discovery (logical names like `"web-api"`) rather than hard-coded URLs.

## Key Conventions

### No version attributes in `.csproj` files
Versions are derived automatically from git tags via **MinVer**. Tag prefix is `v` (e.g. `v1.2.3`). Default pre-release label is `preview`. Never add `<Version>` or `<VersionPrefix>` to a project file.

### Central Package Management
All NuGet package versions live **only** in `Directory.Packages.props`. Individual `.csproj` files use `<PackageReference Include="..." />` with **no `Version` attribute**. `CentralPackageVersionOverrideEnabled=false` — per-project version overrides are not allowed.

### Shared MSBuild properties
`Directory.Build.props` applies to every project:
- `TargetFramework`: `net9.0` — change here to upgrade all projects at once
- `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`
- `EnableNETAnalyzers=true`, `AnalysisMode=All`, `EnforceCodeStyleInBuild=true`

### ServiceDefaults must be referenced by every service
Call `builder.AddServiceDefaults()` in each service's `Program.cs` and `app.MapDefaultEndpoints()` before `app.Run()`. This is what registers health checks and OpenTelemetry.

### Conventional Commits drive versioning
Commit messages control the semver bump on every merge to `main`:
- `BREAKING CHANGE` / `feat!:` / `fix!:` → **major**
- `feat:` → **minor**
- Everything else (`fix:`, `chore:`, `docs:`, etc.) → **patch**

Git tags are created automatically by the Official Build Pipeline — never create or push tags manually.

### Integration tests use the real AppHost
Tests in `Dst.HostApplication.Tests` use `DistributedApplicationTestingBuilder.CreateAsync<Projects.Dst_Aspires_AppHost>()` to start the full application stack. Use `app.ResourceNotifications.WaitForResourceHealthyAsync(...)` before making HTTP calls.

### SDK version pinned in `global.json`
The .NET SDK version (`9.0.300`, `rollForward: latestPatch`) is the single source of truth for developers and all CI pipelines.

## CI Pipelines

| Pipeline | Trigger | Actions |
|---|---|---|
| **PR Build** | PR → `main` | Build + test (gate) |
| **Official Build** | Push to `main` / manual | Tag, build, test, publish artifacts |
| **Official Release** | Manual | Download artifacts, create GitHub Release |

All pipelines use `fetch-depth: 0` for MinVer to read full git history.
