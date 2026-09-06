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

By following these 4 steps, you built a production-grade distributed architecture with zero infrastructure friction:

1. **Isolated Business Logic in Orleans Silo (`Dst.Features`):** Your domain logic lives in the Silo host (`Dst.OrleansSilo.WebApp`). Grains execute in-memory with single-threaded thread safety. As workload grows, Silo nodes can be independently replicated to scale background processing.
2. **Stateless Gateway Layer (`Dst.WebApiApp`):** The HTTP API performs no business logic—it simply forwards calls to the Orleans cluster via `IClusterClient`. It can be independently replicated to handle high HTTP request volume.
3. **Local Cloud-Native Orchestration (`Dst.Aspires.AppHost`):** .NET Aspire orchestrates the API, Silo host, Redis containers, and OpenTelemetry tracing automatically, letting you run a multi-service production topology locally with a single `dotnet run`.
