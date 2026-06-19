# Requirement 07 - Distributed Lock Outside Database Locks

## 1. Simple explanation

The requirement asks us to control concurrency using a distributed lock in places other than database locks. A normal `lock` statement protects only one running application process. If the project is deployed behind load balancing with multiple application instances, each instance has its own memory and its own local locks. Therefore, local locks are not enough for shared business resources.

For this requirement, Redis is used as the distributed lock store. The application acquires an atomic Redis lock before entering a critical business section. The lock is not a database row lock and it is not the same as the inventory lock used in the earlier race-condition requirement.

The implementation protects two e-commerce scenarios:

1. Limited coupon redemption, for example only 5 users may redeem `FLASH-100`.
2. Payment callback idempotency, where the payment provider may send the same callback multiple times.

## 2. Problem before the solution

### Coupon redemption before lock

If 20 users try to redeem a coupon with only 5 available redemptions, many requests can read the same remaining value before any request updates it. This creates over-redemption: more than 5 users succeed and the remaining coupon count can become negative.

### Payment callback before lock

Payment gateways sometimes send the same callback more than once. Without a distributed lock, repeated callbacks can all pass the idempotency check at the same time and capture the same payment multiple times.

## 3. Proposed solutions

### Solution A - Local lock in application memory

A local lock is simple and fast, but it works only inside one application instance. If two backend instances are running behind a load balancer, instance A and instance B do not share the same lock. This solution is not enough for a distributed backend.

### Solution B - Database pessimistic lock

A database lock can serialize access, but the doctor explicitly asked for distributed locks in places other than database locks. Also, using database locks for every business coordination point can increase database pressure.

### Solution C - Redis distributed lock

Redis can create an atomic lock key using `SET NX` semantics. Only the request that creates the key can enter the critical section. Other requests wait or fail cleanly. This lock is shared by all backend instances as long as they connect to the same Redis server.

## 4. Selected solution and reason

The selected solution is Redis distributed locking through `StackExchange.Redis`.

Reason:

- It is outside database locks.
- It works across multiple application instances.
- It is appropriate for short critical sections such as coupon redemption and payment idempotency.
- It fits the Redis dependency already introduced in Requirement 06.
- It can be tested clearly with before/after API demos.

## 5. Files added or modified

### Added files

- `Services/IDistributedLockService.cs`
- `Services/RedisDistributedLockService.cs`
- `Services/InMemoryDistributedLockService.cs`
- `Services/DistributedLockDemoService.cs`
- `Endpoints/DistributedLockEndpoints.cs`
- `DTOs/CouponRedemptionRequest.cs`
- `DTOs/PaymentCallbackRequest.cs`
- `Models/DistributedLockLease.cs`
- `Models/DistributedLockMetricsSnapshot.cs`
- `Models/DistributedLockDemoMetricsSnapshot.cs`
- `Models/CouponStateSnapshot.cs`
- `Tests/JMeter/Requirement07_DistributedLock.jmx`

### Modified files

- `Program.cs`
- `appsettings.json`
- `appsettings.Development.json`

## 6. Main endpoints

### Reset and metrics

```http
POST /distributed-lock/reset
GET  /distributed-lock/metrics
```

### Coupon redemption

```http
POST /demo/distributed-lock/coupon/before
POST /demo/distributed-lock/coupon/after
```

### Payment callback idempotency

```http
POST /demo/distributed-lock/payment/before
POST /demo/distributed-lock/payment/after
```

### Full demo

```http
POST /demo/distributed-lock/all
```

## 7. How to run the project for this requirement

Start Redis:

```powershell
docker compose -f docker-compose.cache.yml up -d
```

Verify Redis is running:

```powershell
docker ps
docker exec -it parallel-ecommerce-redis redis-cli ping
```

Expected output:

```text
PONG
```

Verify configuration in `appsettings.Development.json`:

```json
"Cache": {
  "UseRedis": true,
  "RedisConnectionString": "localhost:6379"
}
```

Run the API:

```powershell
dotnet restore
dotnet run
```

Open Swagger:

```text
http://localhost:5164/swagger
```

## 8. How to test it

### Test 1 - Verify Redis distributed lock provider

Run:

```http
POST /distributed-lock/reset
GET  /distributed-lock/metrics
```

Expected important values:

```json
"redisConfigured": true,
"providerName": "Redis distributed lock using SET NX through StackExchange.Redis"
```

### Test 2 - Coupon before distributed lock

Run:

```http
POST /distributed-lock/reset
POST /demo/distributed-lock/coupon/before
```

Expected result:

```json
"initialAvailableCoupons": 5,
"concurrentUsers": 20,
"successCount": 20,
"remainingRedemptions": -15
```

This proves the problem: more users redeemed the coupon than allowed.

### Test 3 - Coupon after distributed lock

Run:

```http
POST /distributed-lock/reset
POST /demo/distributed-lock/coupon/after
```

Expected result:

```json
"initialAvailableCoupons": 5,
"concurrentUsers": 20,
"successCount": 5,
"failedCount": 15,
"remainingRedemptions": 0
```

This proves the solution: Redis serialized the coupon critical section.

### Test 4 - Payment callback before distributed lock

Run:

```http
POST /distributed-lock/reset
POST /demo/distributed-lock/payment/before
```

Expected result:

```json
"repeatedCallbacks": 20,
"capturedCount": 20,
"capturedPaymentCounter": 20
```

This proves the problem: the same payment can be captured multiple times.

### Test 5 - Payment callback after distributed lock

Run:

```http
POST /distributed-lock/reset
POST /demo/distributed-lock/payment/after
```

Expected result:

```json
"repeatedCallbacks": 20,
"capturedCount": 1,
"capturedPaymentCounter": 1
```

This proves the solution: only one payment callback captured the payment, while duplicates were blocked.

## 9. JMeter test

The JMeter file is:

```text
Tests/JMeter/Requirement07_DistributedLock.jmx
```

It runs the reset, before, after, and metrics endpoints. Open it in JMeter and run it while the API and Redis are running.

## 10. Screenshots to include in the report

Use these names for the screenshots:

```text
Req07_01_Redis_Running_PONG.png
Req07_02_Distributed_Lock_Metrics_Redis.png
Req07_03_Coupon_Before_Over_Redemption.png
Req07_04_Coupon_After_Redis_Lock.png
Req07_05_Payment_Before_Duplicate_Capture.png
Req07_06_Payment_After_Idempotent_Lock.png
Req07_07_JMeter_Summary_Report.png
```

## 11. Final comparison

### Before

```text
Coupon success count: 20 while available coupons: 5
Payment captured count: 20 for the same payment reference
```

### After

```text
Coupon success count: 5 while available coupons: 5
Payment captured count: 1 for the same payment reference
Redis lock acquire/release counters are visible in metrics
```

## 12. Conclusion

The project now uses Redis distributed locks to protect business-level critical sections outside the database. The test proves that concurrency problems appear before the lock and disappear after using the Redis distributed lock.
