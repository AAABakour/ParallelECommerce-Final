# Requirement 08 - Real Database ACID Transaction

## Requirement Text Summary

Requirement 08 asks the checkout operation to behave as one atomic database unit. Payment capture, stock update, and order creation must all succeed together or fail together.

This real database version uses SQL Server transactions through EF Core.

## Problem Being Solved

Checkout contains several dependent database changes:

1. Decrease product stock.
2. Insert payment record.
3. Insert order record.

If the application fails after inserting payment but before inserting order, the database can contain a captured payment without an order. If stock was also decreased, stock can be lost.

The system must prevent partial commits.

## Selected Solution

The selected solution is a real EF Core database transaction with SQL Server.

Implementation details:

- The service begins a database transaction with `BeginTransactionAsync`.
- The transaction uses `IsolationLevel.Serializable`.
- The service loads `ProductEntity` by `productId`.
- It checks stock.
- It decreases stock.
- It inserts `PaymentEntity`.
- It optionally throws an exception after the payment insert to simulate failure.
- It inserts `OrderEntity`.
- It commits the transaction only after all steps succeed.
- Any exception triggers `RollbackAsync`.

## Why This Solution Was Chosen

A database transaction is the correct production-style solution for this requirement because the three changes belong to one business operation.

Reasons:

- SQL Server guarantees atomic commit or rollback.
- EF Core sends the actual inserts and updates to the database.
- `Serializable` isolation gives strong protection for this demo under concurrent access.
- The rollback is not an application-level fake rollback.
- The response reads proof from the database after commit or rollback.

## Files Changed

Main files for this requirement:

```text
Entities/ProductEntity.cs
Entities/OrderEntity.cs
Entities/PaymentEntity.cs
Data/ParallelECommerceDbContext.cs
DTOs/CheckoutRequest.cs
Services/DatabaseTransactionService.cs
Endpoints/DatabaseTransactionEndpoints.cs
Program.cs
appsettings.json
ParallelECommerce.csproj
```

Related older demo kept unchanged:

```text
Services/TransactionIntegrityService.cs
Endpoints/TransactionIntegrityEndpoints.cs
Docs/Requirement08_TransactionIntegrity.md
```

## How This Improves The Older Simulation Demo

The older Requirement 08 demo is still useful because it explains the ACID idea with in-memory state, rollback records, and concurrent checkout simulations. The new real database demo applies the same business rule with SQL Server.

The improvement is that rollback is handled by an actual database transaction. Payment insert, stock update, and order insert are sent to SQL Server inside one transaction. If the simulated failure happens after payment, SQL Server rolls back all changes together. This gives stronger proof for the final delivery while keeping the older explanation endpoints available.

## Endpoints

```http
POST /db-transaction/reset
POST /db-transaction/checkout/after
```

Swagger tag:

```text
Requirement 08 - Real DB ACID Transaction
```

## Request Body

Use this body for a successful checkout:

```json
{
  "productId": 1,
  "quantity": 1,
  "customerEmail": "customer@example.com",
  "paymentReference": "payment-db-req08-success-001",
  "amount": 1750,
  "simulateFailureAfterPayment": false
}
```

Use this body to prove rollback:

```json
{
  "productId": 1,
  "quantity": 1,
  "customerEmail": "customer@example.com",
  "paymentReference": "payment-db-req08-fail-001",
  "amount": 1750,
  "simulateFailureAfterPayment": true
}
```

## Expected Swagger Results

### Reset

Run:

```http
POST /db-transaction/reset
```

Expected important values:

```json
{
  "requirement": "Requirement 08 - Real DB ACID Transaction",
  "productId": 1,
  "stockQuantity": 5
}
```

### Successful transaction

Run:

```http
POST /db-transaction/checkout/after
```

With `simulateFailureAfterPayment` set to `false`.

Expected important values:

```json
{
  "committed": true,
  "rolledBack": false,
  "stockBefore": 5,
  "stockAfter": 4,
  "paymentCreated": true,
  "orderCreated": true,
  "transactionIsolation": "Serializable",
  "invariantHolds": true
}
```

This proves payment, stock update, and order creation committed together.

### Failed transaction with rollback

First run reset again:

```http
POST /db-transaction/reset
```

Then run checkout with `simulateFailureAfterPayment` set to `true`.

Expected important values:

```json
{
  "committed": false,
  "rolledBack": true,
  "stockBefore": 5,
  "stockAfter": 5,
  "paymentCreated": false,
  "orderCreated": false,
  "transactionIsolation": "Serializable",
  "invariantHolds": true
}
```

This proves the payment insert and stock update were rolled back by the database transaction.

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

1. Run `POST /db-transaction/reset`.
2. Run `POST /db-transaction/checkout/after` with `simulateFailureAfterPayment=false`.
3. Confirm `committed=true`, `paymentCreated=true`, `orderCreated=true`, and stock decreased by the purchased quantity.
4. Run `POST /db-transaction/reset` again.
5. Run `POST /db-transaction/checkout/after` with `simulateFailureAfterPayment=true`.
6. Confirm `committed=false`, `rolledBack=true`, `paymentCreated=false`, `orderCreated=false`, and stock returned to the original value.

## How To Explain It In The Interview

Say:

> Requirement 08 needs checkout to be ACID. I added a real SQL Server transaction using EF Core. Inside one transaction, the service reads the product, checks stock, decreases stock, inserts payment, optionally simulates a failure, then inserts the order. It commits only after all steps succeed. If any exception happens, it calls `RollbackAsync`. The response then queries the database again to prove whether payment, order, and stock changes persisted or were rolled back together.

The key point is that rollback is performed by SQL Server transaction handling, not by manually undoing in-memory state.
