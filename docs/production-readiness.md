# 🏭 Production Readiness & CI/CD Operations

This document covers the checklist and operations required to transition a solution built with this template from local development to production.

---

## 🏭 Production Readiness Checklist

Before launching your solution to a live environment, review and execute the following configuration changes:

- [ ] **State Persistence:** Swap the development Redis grain storage (`AddRedis`) with your production database provider (e.g. Azure Table Storage, PostgreSQL, CosmosDB, or AWS DynamoDB via `builder.AddAzureTableClient(...)` or standard Orleans storage packages).
- [ ] **Clustering Provider:** Configure production Orleans clustering for your cloud platform (e.g., Azure Blob/Table, AWS DynamoDB, Kubernetes, or Redis Cluster).
- [ ] **Telemetry & Monitoring:** Route OpenTelemetry export (`OTEL_EXPORTER_OTLP_ENDPOINT`) to your monitoring platform (e.g., Azure Application Insights, Prometheus/Grafana, Datadog).
- [ ] **Secrets Management:** Replace local `appsettings.Development.json` secrets with Azure Key Vault, AWS Secrets Manager, or Kubernetes Secrets.
- [ ] **Health Probes:** Configure your load balancer or orchestrator (Kubernetes / Azure Container Apps / AWS ECS) to probe `/health` (liveness) and `/alive` (readiness).

---

## 🚢 CI/CD Pipelines Overview

The template includes ready-to-run GitHub Actions workflows in `.github/workflows/`:

| Pipeline | Trigger | Purpose |
|---|---|---|
| **PR Build** (`pr_build_pipeline.yml`) | Pull Request $\rightarrow$ `main` | Validates build, static analyzers, and unit/integration tests as a PR gate. |
| **Official Build** (`official_build_pipeline.yml`) | Push to `main` | Builds, runs tests, publishes artifacts, and creates git version tags. |
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
- **Automated CI Check Gate:** Requires the `Build & Test` workflow status check to pass before merging.
- **Review Dismissal:** Automatically dismisses stale pull request approvals when new commits are pushed.
- **CODEOWNERS:** Enforces code owner review requirements when configured.
