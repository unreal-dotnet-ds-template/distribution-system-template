# Template Pack — Developer Guide

This folder contains the NuGet template package for `dotnet new dst-aspire-orleans`.

---

## Build the Package

```powershell
dotnet pack .\template-pack\ -c Release
```

Output goes to `template-pack\nupkg\Dst.AspireOrleans.Template.<version>.nupkg`.

---

## Change the Version

Edit `template-pack\VERSION` — one line, plain semver:

```
0.0.1
```

The `.csproj` reads this file automatically at pack time. No other changes needed.

---

## Install Locally

```powershell
dotnet new install .\template-pack\nupkg\Dst.AspireOrleans.Template.<version>.nupkg
```

Verify it is registered:

```powershell
dotnet new list dst
```

---

## Test the Template

```powershell
mkdir C:\tmp\test-dst
cd C:\tmp\test-dst
dotnet new dst-aspire-orleans -n MyCompany
dotnet build
```

---

## Uninstall

```powershell
dotnet new uninstall Dst.AspireOrleans.Template
```

---

## Iteration Loop

```powershell
dotnet new uninstall Dst.AspireOrleans.Template
dotnet pack .\template-pack\ -c Release
dotnet new install .\template-pack\nupkg\Dst.AspireOrleans.Template.<version>.nupkg
```
