# 🏭 Production Readiness & CI/CD Operations

This document covers the checklist and operations required to transition a solution built with this template from local development to production.

---

## 🏭 Production Readiness Checklist

Before launching your solution to a live environment, review and execute the following configuration changes:

- [ ] **State Persistence:** Replace the Orleans grain-storage provider configured in the Silo (currently `AddRedisGrainStorage("DefaultStore")`) with a provider suitable for your production environment, such as Azure Table, ADO.NET, Cosmos DB, or DynamoDB. Adding an Aspire resource or database client alone does not configure Orleans grain storage; register the matching Orleans provider and point `DefaultStore` to it.
- [ ] **Clustering Provider:** Configure production Orleans clustering for your cloud platform (e.g., Azure Blob/Table, AWS DynamoDB, Kubernetes, or Redis Cluster).
- [ ] **Telemetry & Monitoring:** Route OpenTelemetry export (`OTEL_EXPORTER_OTLP_ENDPOINT`) to your monitoring platform (e.g., Azure Application Insights, Prometheus/Grafana, Datadog).
- [ ] **Secrets Management:** Replace local `appsettings.Development.json` secrets with Azure Key Vault, AWS Secrets Manager, or Kubernetes Secrets.
- [ ] **Health Probes (optional):** If your deployment needs application health probes, expose and secure the endpoints for the environments you deploy. In this template, `MapDefaultEndpoints()` maps them only in Development: `/health` checks all registered health checks, while `/alive` checks only health checks tagged `live`. Enable them in other environments only if they fit your deployment and security requirements.

---

## 🚢 CI/CD Pipelines Overview

The template includes ready-to-run GitHub Actions workflows in `.github/workflows/`:

| Pipeline | Trigger | Purpose |
|---|---|---|
| **PR Build** (`pr_build_pipeline.yml`) | Pull Request $\rightarrow$ `main` | Builds and runs tests. Tests currently use `continue-on-error: true`, so a failing test does not fail the workflow. |
| **Official Build** (`official_build_pipeline.yml`) | Push to `main` | Builds, runs tests, publishes artifacts, and creates git version tags. Tests are currently non-blocking here too. |
| **Official Release** (`official_release_pipeline.yml`) | Manual dispatch | Creates GitHub release from published build artifacts. |

---

## 🔒 Repository Protection Setup (Run Once)

To enforce pull request reviews, linear git history, and automated CI checks on your repository's `main` branch, run the setup script:

```bash
# Requires GitHub CLI (gh auth login)
bash scripts/setup-repo.sh
```

### What `setup-repo.sh` Configures on GitHub:
- **Direct Pushes Blocked:** Even administrators must merge via Pull Requests.
- **Pull Request Approvals:** Requires at least 1 approving code review before merging.
- **Automated CI Check Gate:** Requires the `Build & Test` workflow status check before merging. Since test steps currently use `continue-on-error: true`, this checks the workflow status, not whether every test passed.
- **Review Dismissal:** Automatically dismisses stale pull request approvals when new commits are pushed.
- **CODEOWNERS:** Enforces code owner review requirements when configured.

> [!TIP]
> To make test failures block a PR or official build, remove `continue-on-error: true` from the corresponding test step in its workflow.

---

## 🔗 References & Official Documentation

- **Microsoft Orleans Production & Deployment:**
  - [Orleans Grain Persistence Providers](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence/) — Production storage providers (Azure Table, ADO.NET, Cosmos DB).
  - [Orleans Cluster Management](https://learn.microsoft.com/en-us/dotnet/orleans/host/configuration-guide/) — Production clustering providers and membership table options.
  - [Orleans Deployment & Hosting](https://learn.microsoft.com/en-us/dotnet/orleans/deployment/) — Hosting Orleans silos in production (Azure, Kubernetes, AWS).
- **.NET Aspire Production & Operations:**
  - [.NET Aspire Deployment Overview](https://learn.microsoft.com/en-us/dotnet/aspire/deployment/overview) — Deploying Aspire applications to Azure Container Apps, Kubernetes, and cloud targets.
  - [.NET Aspire Service Defaults & Observability](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/service-defaults) — Health checks (`/health`, `/alive`), metrics, and OTLP exporter configuration.
