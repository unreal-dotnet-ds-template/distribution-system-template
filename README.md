# repo-template

A modern .NET repository template with centralized build configuration, versioning, and CI/CD pipelines.

## Structure

```
/
├── .github/
│   ├── workflows/
│   │   ├── pr_build_pipeline.yml       # PR validation: build + test
│   │   ├── official_build_pipeline.yml # CI on main: build + test + publish artifacts
│   │   └── official_release_pipeline.yml # Release: download artifacts and create GitHub Release
│   └── dependabot.yml                  # Auto-update GitHub Actions and NuGet packages
├── build/                              # Shared MSBuild customizations
├── src/                                # Application source projects
├── tests/                              # Test projects
├── global.json                         # .NET SDK version pin (single source of truth)
├── Directory.Build.props               # MSBuild properties shared across all projects
├── Directory.Build.targets             # MSBuild targets shared across all projects
├── Directory.Packages.props            # Central Package Management — all NuGet versions here
├── NuGet.Config                        # NuGet feed configuration
└── .editorconfig                       # Code style rules
```

## Key Conventions

### Updating the .NET SDK
Edit `global.json` — one change propagates to all developers and all CI pipelines:
```json
{ "sdk": { "version": "9.0.300", "rollForward": "latestPatch" } }
```

### Adding a NuGet Package
1. Add the version to `Directory.Packages.props`:
   ```xml
   <PackageVersion Include="Serilog" Version="4.0.0" />
   ```
2. Reference it in your `.csproj` **without** a version:
   ```xml
   <PackageReference Include="Serilog" />
   ```

### Versioning (Conventional Commits + MinVer)

Git tags are created **automatically by the Official Build Pipeline** on every merge to `main`. You never create tags manually.

The pipeline reads commit messages since the last tag and determines the version bump:

| Commit message pattern | Version bump | Example |
|---|---|---|
| `BREAKING CHANGE` in body, `feat!:`, `fix!:` | **major** | `1.2.3 → 2.0.0` |
| `feat:` or `feat(scope):` | **minor** | `1.2.3 → 1.3.0` |
| `fix:`, `chore:`, `docs:`, anything else | **patch** | `1.2.3 → 1.2.4` |

**Commit message examples:**
```
fix: handle null reference in user service
feat: add export to CSV endpoint
feat!: redesign authentication API
chore: update dependencies
```

The build pipeline creates the tag **before** compiling, so MinVer produces a clean version (e.g. `1.3.0`) rather than a pre-release suffix.

## Pipelines

| Pipeline | Trigger | What it does |
|---|---|---|
| **PR Build** | Pull request → `main` | Build + test — fast PR gate |
| **Official Build** | Push to `main` / manual | Build + test + publish artifacts |
| **Official Release** | Manual | Downloads artifacts from a build run (latest or chosen by Run ID) and creates a GitHub Release |

## Using This Template

After creating a new repository from this template, run the setup script once to lock down the `main` branch:

```bash
# Requires GitHub CLI: https://cli.github.com
bash scripts/setup-repo.sh
```

This applies a branch protection rule that:
- **Blocks all direct pushes to `main`** (including admins)
- Requires a **pull request** with at least **1 approval**
- Requires the **PR Build Pipeline** status check to pass
- Automatically requests review from `CODEOWNERS`
- Dismisses stale approvals when new commits are pushed

> **You must run this once manually.** GitHub does not allow branch protection to be committed as a file — it is a repository setting stored in GitHub itself.

---

### Running a Release
1. Go to **Actions → Official Release Pipeline → Run workflow**
2. Optionally enter a **Build Run ID** (find it in the Official Build Pipeline run history)
3. Leave blank to automatically use the latest successful build
