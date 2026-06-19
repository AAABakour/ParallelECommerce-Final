# Requirement 08 - ACID / Transaction Integrity

## 1. Simple explanation

This requirement asks us to guarantee that a composite e-commerce operation behaves as one atomic unit. In our case, checkout is composed of three business steps:

1. Capture payment.
2. Deduct inventory.
3. Create the order.

The system must not allow a state where payment is captured but the order is not created, or inventory is deducted but the checkout failed. Either all steps commit together, or all side effects are rolled back.

## 2. Important terms

### Transaction

A transaction is a boundary around multiple operations. It makes them behave like one operation.

### ACID

ACID means:

- Atomicity: all steps succeed or all fail.
- Consistency: data remains valid after the operation.
- Isolation: concurrent operations do not corrupt each other.
- Durability: once committed, the result is kept.

### Rollback

Rollback means undoing every change made inside a failed transaction.

### Partial failure

A partial failure happens when some steps succeed and later steps fail. Example: payment is captured and stock is deducted, but order creation fails.

## 3. Problem before the solution

Without a transaction boundary, checkout can leave inconsistent data. If a failure happens after payment and inventory update, the system may keep those side effects even though the order was not created.

In the demo, 10 concurrent users try to checkout, and the system simulates a failure after payment. Before the solution, some operations leave captured payments and deducted stock without orders.

Expected BEFORE result:

```json
"initialStock": 5,
"concurrentUsers": 10,
"finalStock": 0,
"capturedPayments": 5,
"ordersCreated": 0,
"inconsistentPartialOperations": 5
```

This proves the problem: there are payments without orders and lost stock.

## 4. Proposed solutions

### Solution A - Manual checks only

The system can check stock and payment status before creating an order, but checks alone do not solve partial failures. If a failure happens after a side effect, the system still becomes inconsistent.

### Solution B - Database transaction

In a production database-backed system, we would use a database transaction such as `BeginTransaction`, then commit only after payment, stock, and order are all valid. If any step fails, rollback is executed.

### Solution C - Application transaction coordinator for this in-memory demo

Because this academic project uses in-memory state, we implemented a transaction coordinator that records every side effect and rolls it back on failure. It also serializes the commit section so concurrent checkouts do not corrupt the state.

## 5. Selected solution and reason

The selected solution is an application-level transaction coordinator with a rollback log and serialized commit section.

Reason:

- The current project stores demo state in memory, not in a real database.
- The implementation clearly proves the ACID idea: commit all steps or rollback all steps.
- It supports concurrent testing by running 10 checkout attempts at the same time.
- It is directly mappable to a real database transaction if the project is later migrated to EF Core or SQL.

## 6. Files added or modified

### Added files

- `DTOs/CheckoutRequest.cs`
- `Models/TransactionOrderSnapshot.cs`
- `Models/PaymentCaptureSnapshot.cs`
- `Models/TransactionMetricsSnapshot.cs`
- `Services/TransactionIntegrityService.cs`
- `Endpoints/TransactionIntegrityEndpoints.cs`
- `Tests/JMeter/Requirement08_TransactionIntegrity.jmx`

### Modified files

- `Program.cs`

## 7. Main endpoints

### Reset and metrics

```http
POST /transaction/reset
GET  /transaction/metrics
```

### Single checkout test

```http
POST /transaction/checkout/before
POST /transaction/checkout/after
```

### Demo tests

```http
POST /demo/transaction/failure/before
POST /demo/transaction/failure/after
POST /demo/transaction/concurrent/after
POST /demo/transaction/all
```

## 8. How to run the project for this requirement

Redis can remain running because requirements 6 and 7 use it, but requirement 8 itself does not require Redis.

```powershell
docker compose -f docker-compose.cache.yml up -d
docker exec -it parallel-ecommerce-redis redis-cli ping
dotnet restore
dotnet run
```

Open Swagger:

```text
http://localhost:5164/swagger
```

If the terminal shows another port, use that port instead of `5164`.

## 9. How to test it

### Test 1 - Reset

Run:

```http
POST /transaction/reset
```

Expected important values:

```json
"initialStock": 5,
"currentStock": 5,
"ordersCreated": 0,
"capturedPayments": 0
```

### Test 2 - BEFORE: failure without transaction

Run:

```http
POST /demo/transaction/failure/before
```

Expected important values:

```json
"initialStock": 5,
"concurrentUsers": 10,
"finalStock": 0,
"capturedPayments": 5,
"ordersCreated": 0,
"inconsistentPartialOperations": 5
```

This proves that payment and inventory side effects remained although the order was not created.

### Test 3 - AFTER: failure with rollback

Run:

```http
POST /demo/transaction/failure/after
```

Expected important values:

```json
"initialStock": 5,
"concurrentUsers": 10,
"finalStock": 5,
"capturedPayments": 0,
"ordersCreated": 0,
"rollbacks": 10,
"inconsistentPartialOperations": 0
```

This proves that all failed operations were rolled back.

### Test 4 - AFTER: successful concurrent checkout

Run:

```http
POST /demo/transaction/concurrent/after
```

Expected important values:

```json
"initialStock": 5,
"concurrentUsers": 10,
"committedOrders": 5,
"outOfStockFailures": 5,
"finalStock": 0,
"capturedPayments": 5,
"ordersCreated": 5,
"invariantHolds": true
```

This proves that under concurrent access the committed orders remain consistent: every order has a payment and every payment has an order.

## 10. Suggested screenshots

- `Req08_01_Reset.png`
- `Req08_02_Before_Partial_Failure.png`
- `Req08_03_After_Rollback.png`
- `Req08_04_After_Concurrent_Commit.png`
- `Req08_05_Final_Metrics.png`
- `Req08_06_JMeter_Summary_Report.png`

## 11. Report conclusion

Before the solution, a simulated failure after payment caused partial side effects: stock was deducted and payments were captured even though no orders were created. After the solution, the transaction coordinator rolled back every failed checkout, keeping stock, payments, and orders consistent. In the successful concurrent test, exactly 5 orders committed because only 5 units were available, and every committed order had a matching payment.
