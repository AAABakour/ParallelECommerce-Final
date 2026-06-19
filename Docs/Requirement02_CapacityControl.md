# Requirement 02 - Resource Management & Capacity Control

## 1. Problem

The system must not execute an unlimited number of heavy operations at the same time. In a real e-commerce backend, these heavy operations can be payment calls, database queries, invoice generation, inventory updates, or calls to external APIs.

If the backend allows all operations to run immediately, a traffic spike can cause:

- High CPU usage.
- High memory usage.
- ThreadPool pressure.
- Database connection exhaustion.
- Slow responses or service failure.

The requirement is not to remove parallelism completely. The requirement is to control it so the system does not over-consume resources and does not reduce parallelism so much that response time becomes unnecessarily slow.

## 2. Proposed solutions

### Solution A - No capacity control

Every request starts work immediately.

**Pros:**

- Lowest waiting time for small traffic.
- Simple implementation.

**Cons:**

- Dangerous under high load.
- Too many simultaneous operations can exhaust resources.
- No predictable upper bound for active heavy work.

### Solution B - Fixed `SemaphoreSlim` limit

A `SemaphoreSlim` allows only a configured number of heavy operations to run at the same time. Extra operations wait until a slot becomes available.

**Pros:**

- Simple and clear.
- Prevents resource exhaustion.
- Gives a measurable upper bound for active operations.
- Works well for a single backend instance.

**Cons:**

- Extra requests may wait, so total completion time can increase.
- For multiple deployed backend instances, the limit is per instance, not global across all servers.

### Solution C - Queue + background workers

Requests enqueue heavy work, and a fixed number of background workers process the queue.

**Pros:**

- Strong capacity control.
- Good for tasks that do not need immediate user response.

**Cons:**

- Not suitable for operations that must complete before responding to the user.
- Requires queue monitoring and retry/dead-letter handling.

## 3. Chosen solution

The chosen solution for Requirement 02 is **Solution B - `SemaphoreSlim`**.

Reason:

- The requirement asks to control how many parallel operations the system executes.
- `SemaphoreSlim` gives a direct and measurable limit.
- It is simple to demonstrate with JMeter and resource monitoring.
- It keeps the request synchronous from the user's perspective, while still controlling the internal heavy operation.

The configured limit is:

```csharp
public const int MaxParallelOperations = 5;
```

This means that even if 50 users hit the heavy endpoint at the same time, only 5 heavy operations are allowed to run concurrently. The rest wait safely instead of overloading the system.

## 4. Implementation

The implementation is in:

```text
Services/CapacityControlService.cs
Endpoints/CapacityControlEndpoints.cs
```

Main endpoints:

```http
POST /capacity/reset
GET  /capacity/metrics
POST /capacity/before/work
POST /capacity/after/work
```

Demo endpoints:

```http
POST /demo/capacity/before
POST /demo/capacity/after
```

### Before endpoint

`POST /capacity/before/work` runs one heavy operation without any capacity limit.

When many users call this endpoint at the same time, the metric `maxActiveOperationsObserved` can grow with the number of concurrent users.

### After endpoint

`POST /capacity/after/work` runs one heavy operation protected by `SemaphoreSlim`.

When many users call this endpoint at the same time, the metric `maxActiveOperationsObserved` should remain less than or equal to 5.

## 5. How this supports the architecture

This design supports the project architecture because:

- Capacity control is isolated inside `CapacityControlService`.
- Endpoints do not contain synchronization logic directly.
- The limit can be adjusted from a single place.
- The AOP monitoring middleware records response time for every request automatically.
- `ResourceMonitoringService` records CPU, memory, process threads, ThreadPool workers, and active HTTP requests over time.

## 6. Expected test evidence

### Before test

1. Reset monitoring:

```http
POST /monitoring/reset
```

2. Reset capacity metrics:

```http
POST /capacity/reset
```

3. Simulate many concurrent users hitting:

```http
POST /capacity/before/work
```

4. Read:

```http
GET /capacity/metrics
GET /monitoring/resources
GET /monitoring/metrics
```

Expected result:

```text
maxActiveOperationsObserved is high, because there is no capacity limit.
```

### After test

1. Reset monitoring:

```http
POST /monitoring/reset
```

2. Reset capacity metrics:

```http
POST /capacity/reset
```

3. Simulate many concurrent users hitting:

```http
POST /capacity/after/work
```

4. Read:

```http
GET /capacity/metrics
GET /monitoring/resources
GET /monitoring/metrics
```

Expected result:

```text
maxActiveOperationsObserved <= 5
```

This proves that the system is not executing unbounded heavy work.

## 7. Engineering trade-off

The after version may take longer overall because extra operations wait for a semaphore slot. This is acceptable because the purpose of this requirement is not only to minimize latency, but to prevent resource exhaustion and keep the system stable under load.

In the report, this should be explained as:

```text
The solution intentionally trades a small amount of waiting time for predictable resource usage and system stability.
```

## 8. Screenshots to collect later

When the first five requirements are stable, collect these screenshots:

```text
Docs/Screenshots/02_capacity_before_jmeter_summary.png
Docs/Screenshots/02_capacity_before_metrics.png
Docs/Screenshots/02_capacity_before_resources.png
Docs/Screenshots/02_capacity_after_jmeter_summary.png
Docs/Screenshots/02_capacity_after_metrics.png
Docs/Screenshots/02_capacity_after_resources.png
```
