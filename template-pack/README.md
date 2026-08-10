# Aspire + Orleans Distribution System Template

A production-ready `dotnet new` template for building distributed systems with **.NET Aspire** orchestration and **Microsoft Orleans** actor-based business logic.

📦 **Package:** `Dst.AspireOrleans.Template`
🔗 **Repository:** [github.com/unreal-dotnet-ds-template/distribution-system-template](https://github.com/unreal-dotnet-ds-template/distribution-system-template)

---

## What it creates

A ready-to-run solution with the following projects:

| Project | Role |
|---|---|
| `AppHost` | .NET Aspire orchestrator |
| `ApiService` | Orleans client — exposes HTTP API |
| `SiloService` | Orleans silo — hosts your grains |
| `Web` | Blazor frontend |
| `ServiceDefaults` | Shared OpenTelemetry, health checks, service discovery |
| `Tests` | Integration tests using Aspire test hosting |

**Batteries included:**
- Orleans clustering via Redis
- OpenTelemetry (traces, metrics, logs)
- Health check endpoints (`/health`, `/alive`)
- Central NuGet package management
- MinVer automatic versioning from git tags
- GitHub Actions CI/CD pipelines

---

## Install

```shell
dotnet new install Dst.AspireOrleans.Template
```

## Create a new project

```shell
dotnet new dst-aspire-orleans -n MyCompany
cd MyCompany
dotnet run --project AppHost
```

## Uninstall

```shell
dotnet new uninstall Dst.AspireOrleans.Template
```

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker](https://www.docker.com/) (for Redis clustering used by Orleans)
