# Aspire + Orleans Distribution System Template

A production-ready `dotnet new` template for building distributed systems with **.NET Aspire** and **Microsoft Orleans**.

[![NuGet](https://img.shields.io/nuget/v/Dst.AspireOrleans.Template.svg?style=flat-square&label=NuGet)](https://www.nuget.org/packages/Dst.AspireOrleans.Template)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](https://opensource.org/licenses/MIT)

---

## 🎯 What is it?

A pre-configured .NET solution template that combines **Microsoft Orleans** (for distributed virtual actor logic) and **.NET Aspire** (for cloud-native orchestration and telemetry), following modern repository standards out of the box.

---

## 💡 Why use it?

Setting up a distributed Orleans system usually requires configuring clustering, silo hosting, grain storage, local containers, service discovery, and OpenTelemetry from scratch.

This template eliminates the setup overhead: you get a fully functional, orchestrated distributed system with Redis clustering and OpenTelemetry running in seconds.

---

## ✨ Key Benefits

- 🎭 **Orleans Virtual Actors:** Build distributed, concurrent business logic without manual lock management, cache invalidation, or database boilerplate.
- 🔭 **Zero-Friction Local Execution:** .NET Aspire boots Redis, the Orleans Silo, Web API, and the telemetry dashboard with a single command.
- 🧩 **The 3-Folder Rule:** All business logic lives in just 3 projects (`Core`, `Features`, `WebApiApp`). Infrastructure and hosting remain untouched.
- 🧪 **Built-in E2E & Functional Testing:** Test full distributed flows using Aspire's test hosting library in C# without third-party test runners.
- 📦 **Modern .NET Repository Standards:** Central Package Management (`Directory.Packages.props`), pinned .NET 9 SDK, MinVer semantic versioning, and GitHub Actions CI/CD.

---

## ⚡ How to Use

```bash
# 1. Install the template
dotnet new install Dst.AspireOrleans.Template

# 2. Create your project
dotnet new dst-aspire-orleans -n MyPaymentSystem

# 3. Run everything
cd MyPaymentSystem
dotnet run --project src/Aspires/MyPaymentSystem.Aspires.AppHost

# 4. Open README.md file to learn what's next
```

---

## 📋 Requirements

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/) or [Podman](https://podman.io/) (for local Redis clustering container)

---

🔗 **Full documentation & E2E guide:** [GitHub Repository](https://github.com/unreal-dotnet-ds-template/distribution-system-template)
