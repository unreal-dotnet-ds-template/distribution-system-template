# 🚀 Your First Feature in 5 Minutes (E2E Flow)

This guide walks you through building a distributed **BankAccount** feature (`Deposit` and `GetBalance`) to demonstrate how Microsoft Orleans and .NET Aspire work together in this template.

## Prerequisites

> **Important:** Ensure **Docker Desktop** (or Docker Engine) is installed and running, as .NET Aspire relies on it to launch the Redis container for Orleans clustering and state storage.

## Step 1: Define the Contract in `Dst.Core`

The `Dst.Core` project contains interfaces, grain contracts, and shared DTOs.

Create `src/Dst.Core/Features/BankAccounts/IBankAccountGrain.cs`:

```csharp
namespace Dst.Core.Features.BankAccounts;

public interface IBankAccountGrain : IGrainWithStringKey
{
    Task<decimal> DepositAsync(decimal amount);
    Task<decimal> GetBalanceAsync();
}
```

## Step 2: Implement the Grain in `Dst.Features`

The `Dst.Features` project contains your Orleans grain business logic.

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

## Step 3: Expose HTTP Endpoints in `Dst.WebApiApp`

In `src/Dst.WebApiApp/Program.cs`, map Minimal API endpoints using `IClusterClient` to route requests to the grain:

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

## Step 4: Run & Verify

Launch the full solution orchestrator:

```bash
dotnet run --project src/Aspires/Dst.Aspires.AppHost
```

### 1. Test Endpoints via Scalar UI or `curl`
Open the **Scalar API Reference** at `https://localhost:<port>/scalar/v1`, or run:

```bash
# Deposit $500 into account "acc-101"
curl -X POST https://localhost:<port>/accounts/acc-101/deposit -H "Content-Type: application/json" -d "500"

# Check balance
curl https://localhost:<port>/accounts/acc-101/balance
# Returns: {"accountId":"acc-101","balance":500}
```

### 2. Inspect Distributed Traces
Open the **Aspire Dashboard** (URL displayed in your terminal) and navigate to **Traces**. You will see the complete distributed call trace from the HTTP API $\rightarrow$ Orleans Silo $\rightarrow$ `BankAccountGrain` execution!

> [!TIP]
> **Notice what you did NOT do:** No manual DI registrations for grains, no database connection string setup, no concurrency locks, and no manual Docker Compose scripts. Orleans and Aspire handle hosting, clustering, and lifetime management automatically.

---

## 🎯 What Did You Achieve?

This tutorial demonstrates the end-to-end request path: HTTP request → API → Orleans grain. It is a learning example, not a production-ready account system. The balance lives only in the grain's memory, so it is not durable across deactivation or a silo restart.

To make account balances durable, add Orleans grain persistence and choose a storage provider for your deployment. The tutorial keeps the grain deliberately small so you can focus on contracts, grain calls, and the API-to-Orleans flow.

---

## 🔗 References & Official Documentation

- **Microsoft Orleans (Grains & Clients):**
  - [Developing Orleans Grains](https://learn.microsoft.com/en-us/dotnet/orleans/grains/) — Defining grain interfaces, key types (`IGrainWithStringKey`), and grain methods.
  - [Orleans External Clients & IClusterClient](https://learn.microsoft.com/en-us/dotnet/orleans/grains/external-tasks-and-grains) — Connecting frontends and APIs to an Orleans cluster via `IClusterClient`.
- **.NET Aspire (Orchestration & Caching):**
  - [.NET Aspire Overview](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview) — Getting started with .NET Aspire local orchestration.
  - [.NET Aspire StackExchange Redis Integration](https://learn.microsoft.com/en-us/dotnet/aspire/caching/stackexchange-redis-integration) — Local containerized Redis setup for state and clustering.
