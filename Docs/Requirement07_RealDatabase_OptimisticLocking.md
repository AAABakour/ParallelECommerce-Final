# Requirement 07 - Real Database Optimistic Locking

## Requirement Text Summary

Requirement 07 asks the system to protect sensitive inventory quantities during concurrent updates. When many users try to buy the same product at the same time, the backend must prevent overselling.

This real database version uses SQL Server and EF Core optimistic concurrency with the `ProductEntity.RowVersion` column.

## Problem Being Solved

Inventory is a shared resource. If 10 users read stock `5` at the same time and all decrement it without concurrency control, the system can sell more items than exist.

The dangerous sequence is:

1. Multiple requests read the same stock value.
2. Each request believes stock is available.
3. Each request subtracts stock.
4. More than 5 purchases may succeed.

The result is overselling and incorrect stock.

## Selected Solution

The selected solution is EF Core optimistic locking with SQL Server `rowversion`.

Implementation details:

- `ProductEntity.RowVersion` is configured with `IsRowVersion()`.
- Each concurrent purchase attempt loads `ProductEntity` Id `1`.
- Each attempt decreases stock by `1`.
- `SaveChangesAsync` sends the original row version to SQL Server.
- If another request already updated the row, EF Core throws `DbUpdateConcurrencyException`.
- The service counts the conflict and retries up to 5 times.
- If stock is gone after retrying, the result is `OutOfStock`.

## Why This Solution Was Chosen

Optimistic locking is a good fit for inventory demos because it proves real database concurrency without manually locking the row before every read.

Reasons:

- It uses the database as the source of truth.
- It prevents lost updates.
- It allows concurrent requests to try work naturally.
- Conflicts are detected by SQL Server and EF Core, not faked in memory.
- It produces clear proof: only 5 successful purchases are possible from stock 5.

## Files Changed

Main files for this requirement:

```text
Entities/ProductEntity.cs
Data/ParallelECommerceDbContext.cs
Services/DatabaseConcurrencyService.cs
Endpoints/DatabaseConcurrencyEndpoints.cs
Program.cs
appsettings.json
ParallelECommerce.csproj
```

Related older demo kept unchanged:

```text
Services/DistributedLockDemoService.cs
Endpoints/DistributedLockEndpoints.cs
Docs/Requirement07_DistributedLock.md
```

## Endpoints

```http
POST /db-concurrency/reset
POST /demo/db-concurrency/stock/after
```

Swagger tag:

```text
Requirement 07 - Real DB Optimistic Locking
```

## Expected Swagger Results

### Reset

Run:

```http
POST /db-concurrency/reset
```

Expected important values:

```json
{
  "requirement": "Requirement 07 - Real DB Optimistic Locking",
  "productId": 1,
  "stockQuantity": 5
}
```

### Optimistic locking demo

Run:

```http
POST /demo/db-concurrency/stock/after
```

Expected important values:

```json
{
  "requirement": "Requirement 07 - Real DB Optimistic Locking",
  "initialStock": 5,
  "concurrentUsers": 10,
  "successCount": 5,
  "outOfStockCount": 5,
  "finalStock": 0,
  "invariantHolds": true
}
```

`conflictCount` should normally be greater than `0` because the demo intentionally starts all 10 users from the same first read. The exact value can vary by machine and SQL Server timing.

The important proof is:

```text
successCount <= initialStock
finalStock == initialStock - successCount
finalStock is never negative
```

## Manual Test Instructions

Run from the project root:

```powershell
dotnet restore
dotnet build
```

Apply EF Core database changes:

```powershell
dotnet ef database update
```

If `dotnet ef` is not installed:

```powershell
dotnet tool install --global dotnet-ef
dotnet ef database update
```

The reset endpoint also calls `EnsureCreatedAsync`, so it can create the LocalDB database for manual demos when no migration has been generated yet.

Run the API:

```powershell
dotnet run
```

Open Swagger:

```text
http://localhost:5164/swagger
```

If the console shows a different port, use that port.

Test in Swagger:

1. Run `POST /db-concurrency/reset`.
2. Run `POST /demo/db-concurrency/stock/after`.
3. Confirm `successCount` is not greater than `5`.
4. Confirm `finalStock` equals `0`.
5. Confirm `invariantHolds` is `true`.
6. Open `perUserResults` and show that some users succeeded and the rest became `OutOfStock` after real retries.

## How To Explain It In The Interview

Say:

> Requirement 07 protects inventory from concurrent overselling. I added a real SQL Server implementation using EF Core optimistic concurrency. The product has a `RowVersion` column configured with `IsRowVersion()`. Ten users try to buy one unit from stock five. They all start concurrently, and when two users try to save based on the same old row version, EF Core throws `DbUpdateConcurrencyException`. The service retries the failed request using the latest database value. At the end, only five purchases can commit, the remaining users receive `OutOfStock`, and the invariant proves no overselling happened.

The key point is that the result is not fake. The conflicts come from real SQL Server row version checks during `SaveChangesAsync`.
