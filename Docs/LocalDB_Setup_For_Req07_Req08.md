# LocalDB Setup For Real Database Requirements 07 and 08

## Purpose

Requirements 07 and 08 now include real SQL Server demos through EF Core:

- Requirement 07 uses optimistic locking with `ProductEntity.RowVersion`.
- Requirement 08 uses a real database transaction for payment, stock update, and order creation.

The project uses SQL Server LocalDB for local testing.

## Connection String

The API reads this connection string from `appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=ParallelECommerceDb;Trusted_Connection=True;MultipleActiveResultSets=False;TrustServerCertificate=True"
}
```

## Required Commands

Run these commands from the project root.

Check LocalDB:

```powershell
sqllocaldb info
```

Create the default LocalDB instance if it does not exist:

```powershell
sqllocaldb create MSSQLLocalDB
```

Start LocalDB:

```powershell
sqllocaldb start MSSQLLocalDB
```

Restore and build:

```powershell
dotnet restore
dotnet build
```

Apply the EF Core migration:

```powershell
dotnet ef database update
```

Run the API:

```powershell
dotnet run
```

Open Swagger:

```text
http://localhost:5164/swagger
```

If the console shows a different port, use that port.

## If sqllocaldb Is Not Recognized

If PowerShell says `sqllocaldb` is not recognized, SQL Server Express LocalDB is not installed or is not available on the PATH.

Install it from Visual Studio Installer:

1. Open Visual Studio Installer.
2. Modify the installed Visual Studio version.
3. Open Individual components.
4. Select SQL Server Express LocalDB.
5. Install the component.
6. Open a new PowerShell window and run `sqllocaldb info` again.

## Swagger Tests

Requirement 07:

```http
POST /db-concurrency/reset
POST /demo/db-concurrency/stock/after
```

Expected proof:

```json
{
  "successCount": 5,
  "outOfStockCount": 5,
  "finalStock": 0,
  "invariantHolds": true
}
```

Requirement 08:

```http
POST /db-transaction/reset
POST /db-transaction/checkout/after
```

Successful checkout body:

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

Rollback checkout body:

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

Expected rollback proof:

```json
{
  "committed": false,
  "rolledBack": true,
  "paymentCreated": false,
  "orderCreated": false,
  "invariantHolds": true
}
```
