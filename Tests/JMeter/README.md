# JMeter tests

Run the API first, then open the `.jmx` files with Apache JMeter.

Default variables inside the plans:

```text
HOST = localhost
PORT = 5164
BASE_PROTOCOL = http
```

For Requirement 06, capture these screenshots for the final report:

```text
Docs/Screenshots/Requirement06_Caching/06_cache_before_demo.png
Docs/Screenshots/Requirement06_Caching/06_cache_after_demo.png
Docs/Screenshots/Requirement06_Caching/06_cache_metrics.png
Docs/Screenshots/Requirement06_Caching/06_jmeter_summary.png
```

For Requirement 07, open:

```text
Tests/JMeter/Requirement07_DistributedLock.jmx
```

For Requirement 08, open:

```text
Tests/JMeter/Requirement08_TransactionIntegrity.jmx
```

Requirement 08 screenshot names:

```text
Req08_01_Reset.png
Req08_02_Before_Partial_Failure.png
Req08_03_After_Rollback.png
Req08_04_After_Concurrent_Commit.png
Req08_05_Final_Metrics.png
Req08_06_JMeter_Summary_Report.png
```

## Requirement 09 - 100 Users Stress Test

File:

```text
Requirement09_Stress100Users_AllOperations.jmx
```

Purpose:

- Simulates 100 concurrent users.
- Covers the main operations: cache, inventory, capacity control, async queue, batch processing, load balancing, Redis distributed lock, and transaction checkout.
- Use Summary Report, Aggregate Report, and View Results Tree as proof screenshots.

Before running:

```powershell
docker compose -f docker-compose.cache.yml up -d
docker exec -it parallel-ecommerce-redis redis-cli ping
dotnet run
```

If the app uses a port other than 5164, edit the `PORT` variable in JMeter.
