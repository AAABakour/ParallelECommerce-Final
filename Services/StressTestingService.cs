using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class StressTestingService
{
    public const int RequiredConcurrentUsers = 100;

    private readonly InventoryService _inventoryService;
    private readonly CachingCatalogService _cachingService;
    private readonly CapacityControlService _capacityService;
    private readonly NotificationQueueService _notificationQueueService;
    private readonly BatchProcessingService _batchProcessingService;
    private readonly LoadBalancingService _loadBalancingService;
    private readonly DistributedLockDemoService _distributedLockService;
    private readonly TransactionIntegrityService _transactionService;
    private readonly PerformanceMetricsService _performanceMetricsService;

    private readonly ConcurrentDictionary<string, StressOperationCounter> _operationCounters = new();
    private readonly object _snapshotLock = new();

    private StressTestMetricsSnapshot _lastSnapshot = new()
    {
        ProviderName = "In-process 100 virtual users exercising all protected backend operations",
        Explanation = "Run POST /demo/stress/100-users to execute the full stress scenario."
    };

    private long _completedUsers;
    private long _userFailures;
    private DateTime? _lastRunAtUtc;

    public StressTestingService(
        InventoryService inventoryService,
        CachingCatalogService cachingService,
        CapacityControlService capacityService,
        NotificationQueueService notificationQueueService,
        BatchProcessingService batchProcessingService,
        LoadBalancingService loadBalancingService,
        DistributedLockDemoService distributedLockService,
        TransactionIntegrityService transactionService,
        PerformanceMetricsService performanceMetricsService)
    {
        _inventoryService = inventoryService;
        _cachingService = cachingService;
        _capacityService = capacityService;
        _notificationQueueService = notificationQueueService;
        _batchProcessingService = batchProcessingService;
        _loadBalancingService = loadBalancingService;
        _distributedLockService = distributedLockService;
        _transactionService = transactionService;
        _performanceMetricsService = performanceMetricsService;
    }

    public StressTestMetricsSnapshot GetLastSnapshot()
    {
        lock (_snapshotLock)
        {
            return _lastSnapshot;
        }
    }

    public async Task<StressTestMetricsSnapshot> ResetAsync(CancellationToken cancellationToken = default)
    {
        await ResetScenarioStateAsync(cancellationToken);

        var snapshot = BuildSnapshot(
            configuredUsers: RequiredConcurrentUsers,
            durationMs: 0,
            dataIntegrityValidated: true,
            dataIntegritySummary: new
            {
                message = "Stress-test state was reset. Run /demo/stress/100-users to execute the 100-user scenario.",
                inventoryProduct1Stock = _inventoryService.GetProduct(1)?.StockQuantity,
                redisCache = _cachingService.GetMetrics().RedisConfigured,
                redisDistributedLock = _distributedLockService.GetMetrics().RedisConfigured
            },
            supportingMetrics: BuildSupportingMetrics());

        SaveSnapshot(snapshot);
        return snapshot;
    }

    public async Task<StressTestMetricsSnapshot> RunOneHundredUsersAsync(CancellationToken cancellationToken = default)
    {
        await ResetScenarioStateAsync(cancellationToken);

        // Let the resource monitor record a baseline sample before the burst starts.
        await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var userTasks = Enumerable.Range(1, RequiredConcurrentUsers)
            .Select(userNumber => RunVirtualUserJourneyAsync(userNumber, cancellationToken))
            .ToArray();

        await Task.WhenAll(userTasks);
        stopwatch.Stop();

        // Let the background resource monitor capture the tail of the load.
        await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);

        var dataIntegrity = ValidateDataIntegrity();
        var snapshot = BuildSnapshot(
            configuredUsers: RequiredConcurrentUsers,
            durationMs: stopwatch.Elapsed.TotalMilliseconds,
            dataIntegrityValidated: dataIntegrity.Validated,
            dataIntegritySummary: dataIntegrity.Summary,
            supportingMetrics: BuildSupportingMetrics());

        SaveSnapshot(snapshot);
        return snapshot;
    }

    private async Task ResetScenarioStateAsync(CancellationToken cancellationToken)
    {
        _operationCounters.Clear();
        Interlocked.Exchange(ref _completedUsers, 0);
        Interlocked.Exchange(ref _userFailures, 0);

        _performanceMetricsService.Reset();
        _capacityService.ResetMetrics();
        _notificationQueueService.ResetMetrics();
        _batchProcessingService.Reset();
        _loadBalancingService.Reset();
        _distributedLockService.ResetState();
        _transactionService.ResetState();

        // Product 1 is used by the inventory part of the stress scenario.
        // 200 units are enough for all 100 protected purchases, so any failure here is unexpected.
        _inventoryService.ResetStock(1, 200);
        await _cachingService.ResetAsync(cancellationToken);
        await _cachingService.InvalidateProductAsync(1, cancellationToken);

        _lastRunAtUtc = null;
    }

    private async Task RunVirtualUserJourneyAsync(int userNumber, CancellationToken cancellationToken)
    {
        try
        {
            await RecordOperationAsync(
                "Caching - product details from Redis cache",
                () => _cachingService.GetProductCachedAsync(1, cancellationToken));

            await RecordOperationAsync(
                "Caching - popular products from Redis cache",
                () => _cachingService.GetPopularProductsCachedAsync(3, cancellationToken));

            await RecordOperationAsync(
                "Caching - stock snapshot from Redis cache",
                () => _cachingService.GetStockCachedAsync(1, cancellationToken));

            await RecordOperationAsync(
                "Inventory - protected stock purchase",
                () => Task.FromResult<object?>(new { success = _inventoryService.PurchaseAfter(1, 1) }),
                result => !TryReadBool(result, "success"));

            await RecordOperationAsync(
                "Capacity control - bounded heavy operation",
                async () => await _capacityService.RunWithCapacityControlAsync(80));

            await RecordOperationAsync(
                "Async queue - enqueue notification job",
                async () =>
                {
                    await _notificationQueueService.EnqueueAsync(new NotificationJob
                    {
                        CustomerEmail = $"stress-user-{userNumber}@example.com",
                        Message = $"Stress test invoice notification for virtual user {userNumber}."
                    }, cancellationToken);

                    return new { queued = true };
                });

            await RecordOperationAsync(
                "Batch processing - parallel chunk calculation",
                () => _batchProcessingService.ProcessInParallelChunksAsync(totalRecords: 10, chunkSize: 5, maxParallelChunks: 2));

            await RecordOperationAsync(
                "Load balancing - round robin routing",
                async () => await _loadBalancingService.RouteSingleRequestWithRoundRobinAsync());

            await RecordOperationAsync(
                "Distributed lock - limited coupon redemption",
                () => _distributedLockService.RedeemCouponAfterAsync(
                    $"stress-user-{userNumber}",
                    "FLASH-100",
                    cancellationToken),
                result => !TryReadBool(result, "success"));

            // Ten payment references with ten callbacks each. The expected outcome is exactly one capture per reference.
            var paymentReference = $"payment-req09-{(userNumber % 10):D2}";
            await RecordOperationAsync(
                "Distributed lock - idempotent payment callback",
                () => _distributedLockService.ProcessPaymentAfterAsync(
                    paymentReference,
                    $"callback-{userNumber}",
                    99.99m,
                    cancellationToken),
                result => !TryReadBool(result, "captured"));

            await RecordOperationAsync(
                "Transaction integrity - ACID checkout",
                () => _transactionService.CheckoutAfterAsync(
                    productId: 1,
                    quantity: 1,
                    customerEmail: $"stress-user-{userNumber}@example.com",
                    paymentReference: $"payment-req09-checkout-{userNumber}",
                    amount: 99.99m,
                    simulateFailureAfterPayment: false,
                    cancellationToken),
                result => !TryReadBool(result, "success"));

            Interlocked.Increment(ref _completedUsers);
        }
        catch
        {
            Interlocked.Increment(ref _userFailures);
        }
    }

    private async Task RecordOperationAsync(
        string operationName,
        Func<Task<object?>> operation,
        Func<object?, bool>? isExpectedBusinessFailure = null)
    {
        var counter = _operationCounters.GetOrAdd(operationName, static name => new StressOperationCounter(name));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await operation();
            stopwatch.Stop();

            var businessFailure = isExpectedBusinessFailure?.Invoke(result) ?? false;
            counter.Record(stopwatch.Elapsed, businessFailure, unexpectedFailure: false);
        }
        catch
        {
            stopwatch.Stop();
            counter.Record(stopwatch.Elapsed, expectedBusinessFailure: false, unexpectedFailure: true);
        }
    }

    private (bool Validated, object Summary) ValidateDataIntegrity()
    {
        var product = _inventoryService.GetProduct(1);
        var distributedLockMetrics = _distributedLockService.GetMetrics();
        var transactionMetrics = _transactionService.GetMetrics();
        var notificationQueueStatus = _notificationQueueService.GetStatus();
        var loadBalancingMetrics = _loadBalancingService.GetMetricsSnapshot();

        const int expectedInventoryStock = 100;
        const int expectedCouponSuccesses = 5;
        const int expectedProcessedPaymentReferences = 10;
        const int expectedTransactionCommits = 5;

        var inventoryOk = product?.StockQuantity == expectedInventoryStock;
        var couponOk = distributedLockMetrics.CouponAfterSuccesses == expectedCouponSuccesses
            && distributedLockMetrics.Coupons.FirstOrDefault(coupon => coupon.Code == "FLASH-100")?.RemainingRedemptions == 0;
        var paymentOk = distributedLockMetrics.PaymentAfterCaptures == expectedProcessedPaymentReferences
            && distributedLockMetrics.ProcessedPaymentReferences == expectedProcessedPaymentReferences;
        var transactionOk = transactionMetrics.InconsistentPartialOperations == 0
            && transactionMetrics.AfterCommits == expectedTransactionCommits
            && transactionMetrics.OrdersCreated == expectedTransactionCommits
            && transactionMetrics.CapturedPayments == expectedTransactionCommits;
        var queueOk = notificationQueueStatus.QueuedJobs >= RequiredConcurrentUsers;
        var loadBalancingOk = loadBalancingMetrics.TotalRequests >= RequiredConcurrentUsers
            && loadBalancingMetrics.FailedRequests == 0
            && loadBalancingMetrics.HealthyServers == 3;

        var validated = inventoryOk
            && couponOk
            && paymentOk
            && transactionOk
            && queueOk
            && loadBalancingOk;

        return (validated, new
        {
            inventory = new
            {
                expectedFinalStock = expectedInventoryStock,
                actualFinalStock = product?.StockQuantity,
                passed = inventoryOk
            },
            distributedCoupon = new
            {
                expectedSuccessfulRedemptions = expectedCouponSuccesses,
                actualSuccessfulRedemptions = distributedLockMetrics.CouponAfterSuccesses,
                remainingRedemptions = distributedLockMetrics.Coupons.FirstOrDefault(coupon => coupon.Code == "FLASH-100")?.RemainingRedemptions,
                passed = couponOk
            },
            distributedPayment = new
            {
                expectedProcessedPaymentReferences,
                actualCaptures = distributedLockMetrics.PaymentAfterCaptures,
                actualProcessedPaymentReferences = distributedLockMetrics.ProcessedPaymentReferences,
                passed = paymentOk
            },
            transactionIntegrity = new
            {
                expectedCommittedOrders = expectedTransactionCommits,
                actualCommittedOrders = transactionMetrics.AfterCommits,
                ordersCreated = transactionMetrics.OrdersCreated,
                capturedPayments = transactionMetrics.CapturedPayments,
                inconsistentPartialOperations = transactionMetrics.InconsistentPartialOperations,
                passed = transactionOk
            },
            asyncQueue = new
            {
                expectedQueuedJobsAtLeast = RequiredConcurrentUsers,
                queuedJobs = notificationQueueStatus.QueuedJobs,
                currentQueueDepth = notificationQueueStatus.CurrentQueueDepth,
                passed = queueOk
            },
            loadBalancing = new
            {
                expectedRequestsAtLeast = RequiredConcurrentUsers,
                totalRequests = loadBalancingMetrics.TotalRequests,
                failedRequests = loadBalancingMetrics.FailedRequests,
                healthyServers = loadBalancingMetrics.HealthyServers,
                passed = loadBalancingOk
            },
            conclusion = validated
                ? "The 100-user stress run completed without system errors or data loss. Expected business failures were bounded and safe."
                : "One or more integrity checks failed. Review the detailed fields above."
        });
    }

    private StressTestMetricsSnapshot BuildSnapshot(
        int configuredUsers,
        double durationMs,
        bool dataIntegrityValidated,
        object dataIntegritySummary,
        object supportingMetrics)
    {
        var operationSnapshots = _operationCounters.Values
            .Select(counter => counter.ToSnapshot())
            .OrderBy(snapshot => snapshot.OperationName)
            .ToList();

        var totalAttempts = operationSnapshots.Sum(snapshot => snapshot.Attempts);
        var successfulOperations = operationSnapshots.Sum(snapshot => snapshot.SuccessfulOperations);
        var expectedBusinessFailures = operationSnapshots.Sum(snapshot => snapshot.ExpectedBusinessFailures);
        var unexpectedFailures = operationSnapshots.Sum(snapshot => snapshot.UnexpectedFailures);
        var completedUsers = (int)Interlocked.Read(ref _completedUsers);
        var userFailures = (int)Interlocked.Read(ref _userFailures);
        var stabilityPassed = completedUsers == configuredUsers
            && userFailures == 0
            && unexpectedFailures == 0
            && dataIntegrityValidated;

        var safeDurationMs = Math.Max(durationMs, 1);
        _lastRunAtUtc = durationMs > 0 ? DateTime.UtcNow : _lastRunAtUtc;

        return new StressTestMetricsSnapshot
        {
            ProviderName = "In-process 100 virtual users + JMeter-ready external test plan",
            ConfiguredConcurrentUsers = configuredUsers,
            CompletedUsers = completedUsers,
            UserFailures = userFailures,
            TotalOperationAttempts = totalAttempts,
            SuccessfulOperations = successfulOperations,
            ExpectedBusinessFailures = expectedBusinessFailures,
            UnexpectedFailures = unexpectedFailures,
            DurationMs = Math.Round(durationMs, 2),
            ThroughputOperationsPerSecond = Math.Round(totalAttempts / (safeDurationMs / 1000d), 2),
            StabilityPassed = stabilityPassed,
            DataIntegrityValidated = dataIntegrityValidated,
            LastRunAtUtc = _lastRunAtUtc,
            Operations = operationSnapshots,
            ResourceSummary = BuildResourceSummary(),
            DataIntegritySummary = dataIntegritySummary,
            SupportingMetrics = supportingMetrics,
            Explanation = "Requirement 09 proves that at least 100 concurrent users can exercise the backend operations without application crash or data loss. Expected business failures such as sold-out coupons and out-of-stock checkouts are counted separately from system errors."
        };
    }

    private object BuildSupportingMetrics()
    {
        return new
        {
            cache = _cachingService.GetMetrics(),
            capacity = _capacityService.GetMetrics(),
            queue = _notificationQueueService.GetStatus(),
            batch = _batchProcessingService.GetMetrics(),
            loadBalancer = _loadBalancingService.GetMetricsSnapshot(),
            distributedLock = _distributedLockService.GetMetrics(),
            transaction = _transactionService.GetMetrics(),
            monitoring = _performanceMetricsService.GetSummary()
        };
    }

    private StressResourceSummary BuildResourceSummary()
    {
        var samples = _performanceMetricsService.GetResourceSamples();

        if (samples.Count == 0)
        {
            return new StressResourceSummary();
        }

        return new StressResourceSummary
        {
            SampleCount = samples.Count,
            FirstSampleAtUtc = samples.Min(sample => sample.TimestampUtc),
            LastSampleAtUtc = samples.Max(sample => sample.TimestampUtc),
            PeakCpuUsagePercent = Math.Round(samples.Max(sample => sample.CpuUsagePercent), 2),
            AverageCpuUsagePercent = Math.Round(samples.Average(sample => sample.CpuUsagePercent), 2),
            PeakManagedMemoryMb = Math.Round(samples.Max(sample => sample.ManagedMemoryMb), 2),
            PeakWorkingSetMemoryMb = Math.Round(samples.Max(sample => sample.WorkingSetMemoryMb), 2),
            PeakProcessThreadCount = samples.Max(sample => sample.ProcessThreadCount),
            PeakThreadPoolBusyWorkers = samples.Max(sample => sample.ThreadPoolBusyWorkers),
            TotalHttpRequestsObserved = samples.Max(sample => sample.TotalHttpRequests),
            FailedHttpRequestsObserved = samples.Max(sample => sample.FailedHttpRequests)
        };
    }

    private void SaveSnapshot(StressTestMetricsSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            _lastSnapshot = snapshot;
        }
    }

    private static bool TryReadBool(object? result, string propertyName)
    {
        if (result is null)
        {
            return false;
        }

        if (result is bool value)
        {
            return value;
        }

        var property = result.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        return property?.GetValue(result) is bool propertyValue && propertyValue;
    }

    private sealed class StressOperationCounter
    {
        private long _attempts;
        private long _successes;
        private long _expectedBusinessFailures;
        private long _unexpectedFailures;
        private long _totalLatencyTicks;
        private long _maxLatencyTicks;

        public StressOperationCounter(string operationName)
        {
            OperationName = operationName;
        }

        public string OperationName { get; }

        public void Record(TimeSpan latency, bool expectedBusinessFailure, bool unexpectedFailure)
        {
            Interlocked.Increment(ref _attempts);
            Interlocked.Add(ref _totalLatencyTicks, latency.Ticks);

            if (unexpectedFailure)
            {
                Interlocked.Increment(ref _unexpectedFailures);
            }
            else if (expectedBusinessFailure)
            {
                Interlocked.Increment(ref _expectedBusinessFailures);
            }
            else
            {
                Interlocked.Increment(ref _successes);
            }

            UpdateMaxLatency(latency.Ticks);
        }

        public StressOperationMetricsSnapshot ToSnapshot()
        {
            var attempts = Interlocked.Read(ref _attempts);
            var totalLatencyTicks = Interlocked.Read(ref _totalLatencyTicks);

            return new StressOperationMetricsSnapshot
            {
                OperationName = OperationName,
                Attempts = attempts,
                SuccessfulOperations = Interlocked.Read(ref _successes),
                ExpectedBusinessFailures = Interlocked.Read(ref _expectedBusinessFailures),
                UnexpectedFailures = Interlocked.Read(ref _unexpectedFailures),
                AverageLatencyMs = attempts == 0
                    ? 0
                    : Math.Round(TimeSpan.FromTicks(totalLatencyTicks).TotalMilliseconds / attempts, 2),
                MaxLatencyMs = Math.Round(TimeSpan.FromTicks(Interlocked.Read(ref _maxLatencyTicks)).TotalMilliseconds, 2)
            };
        }

        private void UpdateMaxLatency(long latencyTicks)
        {
            long previousMax;
            do
            {
                previousMax = Interlocked.Read(ref _maxLatencyTicks);
                if (latencyTicks <= previousMax)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _maxLatencyTicks, latencyTicks, previousMax) != previousMax);
        }
    }
}
