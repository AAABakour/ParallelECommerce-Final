using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class DistributedLockDemoService
{
    private const string DefaultCouponCode = "FLASH-100";
    private const string DefaultPaymentReference = "payment-req07-001";
    private const int DefaultCouponCapacity = 5;
    private const int ConcurrentActors = 20;

    private readonly IDistributedLockService _lockService;
    private readonly IConfiguration _configuration;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, CouponState> _coupons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _processedPaymentReferences = new(StringComparer.OrdinalIgnoreCase);

    private long _couponBeforeAttempts;
    private long _couponBeforeSuccesses;
    private long _couponAfterAttempts;
    private long _couponAfterSuccesses;
    private long _paymentBeforeAttempts;
    private long _paymentBeforeCaptures;
    private long _paymentAfterAttempts;
    private long _paymentAfterCaptures;
    private int _capturedPaymentCounter;
    private DateTime _lastResetAtUtc = DateTime.UtcNow;

    public DistributedLockDemoService(IDistributedLockService lockService, IConfiguration configuration)
    {
        _lockService = lockService;
        _configuration = configuration;
        ResetState();
    }

    public object ResetState()
    {
        lock (_stateLock)
        {
            _coupons.Clear();
            _coupons[DefaultCouponCode] = new CouponState
            {
                Code = DefaultCouponCode,
                RemainingRedemptions = DefaultCouponCapacity,
                TotalSuccessfulRedemptions = 0
            };

            _processedPaymentReferences.Clear();
            _capturedPaymentCounter = 0;
        }

        Interlocked.Exchange(ref _couponBeforeAttempts, 0);
        Interlocked.Exchange(ref _couponBeforeSuccesses, 0);
        Interlocked.Exchange(ref _couponAfterAttempts, 0);
        Interlocked.Exchange(ref _couponAfterSuccesses, 0);
        Interlocked.Exchange(ref _paymentBeforeAttempts, 0);
        Interlocked.Exchange(ref _paymentBeforeCaptures, 0);
        Interlocked.Exchange(ref _paymentAfterAttempts, 0);
        Interlocked.Exchange(ref _paymentAfterCaptures, 0);
        _lockService.ResetMetrics();
        _lastResetAtUtc = DateTime.UtcNow;

        return new
        {
            message = "Distributed-lock demo state and metrics were reset.",
            metrics = GetMetrics()
        };
    }

    public async Task<object> RedeemCouponBeforeAsync(
        string userId,
        string couponCode = DefaultCouponCode,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _couponBeforeAttempts);
        CouponState? coupon;
        int remainingBeforeRead;

        lock (_stateLock)
        {
            coupon = GetOrCreateCoupon(couponCode);
            remainingBeforeRead = coupon.RemainingRedemptions;
        }

        if (remainingBeforeRead <= 0)
        {
            return new
            {
                mode = "BEFORE - Coupon redemption without distributed lock",
                userId,
                couponCode,
                success = false,
                reason = "Coupon sold out."
            };
        }

        // Artificial delay to allow many concurrent requests to read the same remaining value.
        await Task.Delay(80, cancellationToken);

        lock (_stateLock)
        {
            coupon.RemainingRedemptions--;
            coupon.TotalSuccessfulRedemptions++;
        }

        Interlocked.Increment(ref _couponBeforeSuccesses);

        return new
        {
            mode = "BEFORE - Coupon redemption without distributed lock",
            userId,
            couponCode,
            success = true,
            remainingSeenBeforeDelay = remainingBeforeRead,
            problem = "Multiple users can see the same coupon availability and all redeem it, causing over-redemption."
        };
    }

    public async Task<object> RedeemCouponAfterAsync(
        string userId,
        string couponCode = DefaultCouponCode,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _couponAfterAttempts);

        var resourceName = $"coupon:{couponCode}";
        var lease = await _lockService.TryAcquireAsync(
            resourceName,
            leaseTime: TimeSpan.FromSeconds(10),
            waitTimeout: TimeSpan.FromSeconds(3),
            cancellationToken);

        if (lease is null)
        {
            return new
            {
                mode = "AFTER - Coupon redemption with Redis distributed lock",
                userId,
                couponCode,
                success = false,
                reason = "Could not acquire the distributed lock before timeout."
            };
        }

        await using (lease)
        {
            await Task.Delay(25, cancellationToken);

            int remainingAfter;
            lock (_stateLock)
            {
                var coupon = GetOrCreateCoupon(couponCode);

                if (coupon.RemainingRedemptions <= 0)
                {
                    return new
                    {
                        mode = "AFTER - Coupon redemption with Redis distributed lock",
                        userId,
                        couponCode,
                        success = false,
                        lockResource = resourceName,
                        reason = "Coupon sold out after serialized access."
                    };
                }

                coupon.RemainingRedemptions--;
                coupon.TotalSuccessfulRedemptions++;
                remainingAfter = coupon.RemainingRedemptions;
            }

            Interlocked.Increment(ref _couponAfterSuccesses);

            return new
            {
                mode = "AFTER - Coupon redemption with Redis distributed lock",
                userId,
                couponCode,
                success = true,
                lockResource = resourceName,
                lockProvider = _lockService.GetMetrics().ProviderName,
                remainingAfter,
                explanation = "Only one request at a time can redeem this coupon because the lock key is stored in Redis."
            };
        }
    }

    public async Task<object> ProcessPaymentBeforeAsync(
        string paymentReference = DefaultPaymentReference,
        string callbackId = "callback-1",
        decimal amount = 99.99m,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _paymentBeforeAttempts);
        bool alreadyProcessed;

        lock (_stateLock)
        {
            alreadyProcessed = _processedPaymentReferences.Contains(paymentReference);
        }

        if (alreadyProcessed)
        {
            return new
            {
                mode = "BEFORE - Payment callback without distributed lock",
                paymentReference,
                callbackId,
                captured = false,
                reason = "Duplicate callback was detected."
            };
        }

        // Artificial delay: several callbacks can pass the idempotency check before the first one stores the reference.
        await Task.Delay(80, cancellationToken);

        lock (_stateLock)
        {
            _processedPaymentReferences.Add(paymentReference);
            _capturedPaymentCounter++;
        }

        Interlocked.Increment(ref _paymentBeforeCaptures);

        return new
        {
            mode = "BEFORE - Payment callback without distributed lock",
            paymentReference,
            callbackId,
            amount,
            captured = true,
            problem = "Repeated gateway callbacks can pass the idempotency check together and capture the same payment more than once."
        };
    }

    public async Task<object> ProcessPaymentAfterAsync(
        string paymentReference = DefaultPaymentReference,
        string callbackId = "callback-1",
        decimal amount = 99.99m,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _paymentAfterAttempts);

        var resourceName = $"payment:{paymentReference}";
        var lease = await _lockService.TryAcquireAsync(
            resourceName,
            leaseTime: TimeSpan.FromSeconds(10),
            waitTimeout: TimeSpan.FromSeconds(3),
            cancellationToken);

        if (lease is null)
        {
            return new
            {
                mode = "AFTER - Payment callback with Redis distributed lock",
                paymentReference,
                callbackId,
                captured = false,
                reason = "Could not acquire the distributed lock before timeout."
            };
        }

        await using (lease)
        {
            await Task.Delay(25, cancellationToken);

            lock (_stateLock)
            {
                if (_processedPaymentReferences.Contains(paymentReference))
                {
                    return new
                    {
                        mode = "AFTER - Payment callback with Redis distributed lock",
                        paymentReference,
                        callbackId,
                        captured = false,
                        lockResource = resourceName,
                        reason = "Duplicate callback blocked after serialized idempotency check."
                    };
                }

                _processedPaymentReferences.Add(paymentReference);
                _capturedPaymentCounter++;
            }

            Interlocked.Increment(ref _paymentAfterCaptures);

            return new
            {
                mode = "AFTER - Payment callback with Redis distributed lock",
                paymentReference,
                callbackId,
                amount,
                captured = true,
                lockResource = resourceName,
                lockProvider = _lockService.GetMetrics().ProviderName,
                explanation = "The idempotency check is protected by a Redis lock, so only one callback captures the payment."
            };
        }
    }

    public async Task<object> DemoCouponBeforeAsync(CancellationToken cancellationToken = default)
    {
        ResetState();

        var tasks = Enumerable.Range(1, ConcurrentActors)
            .Select(userNumber => RedeemCouponBeforeAsync($"user-{userNumber}", DefaultCouponCode, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(IsSuccess);

        return new
        {
            mode = "BEFORE - Concurrent coupon redemption without distributed lock",
            couponCode = DefaultCouponCode,
            initialAvailableCoupons = DefaultCouponCapacity,
            concurrentUsers = ConcurrentActors,
            successCount,
            finalCouponState = GetCouponSnapshot(DefaultCouponCode),
            problem = "Success count becomes greater than available coupons because no distributed lock serialized access to the shared coupon resource.",
            results
        };
    }

    public async Task<object> DemoCouponAfterAsync(CancellationToken cancellationToken = default)
    {
        ResetState();

        var tasks = Enumerable.Range(1, ConcurrentActors)
            .Select(userNumber => RedeemCouponAfterAsync($"user-{userNumber}", DefaultCouponCode, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(IsSuccess);
        var failedCount = results.Length - successCount;

        return new
        {
            mode = "AFTER - Concurrent coupon redemption with Redis distributed lock",
            couponCode = DefaultCouponCode,
            initialAvailableCoupons = DefaultCouponCapacity,
            concurrentUsers = ConcurrentActors,
            successCount,
            failedCount,
            finalCouponState = GetCouponSnapshot(DefaultCouponCode),
            lockMetrics = _lockService.GetMetrics(),
            explanation = "The Redis lock allows only one request at a time to check and decrement coupon availability. Exactly five redemptions succeed and the rest fail cleanly.",
            results
        };
    }

    public async Task<object> DemoPaymentBeforeAsync(CancellationToken cancellationToken = default)
    {
        ResetState();

        var tasks = Enumerable.Range(1, ConcurrentActors)
            .Select(callbackNumber => ProcessPaymentBeforeAsync(
                DefaultPaymentReference,
                $"callback-{callbackNumber}",
                99.99m,
                cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var capturedCount = results.Count(IsCaptured);

        return new
        {
            mode = "BEFORE - Repeated payment callbacks without distributed lock",
            paymentReference = DefaultPaymentReference,
            repeatedCallbacks = ConcurrentActors,
            capturedCount,
            capturedPaymentCounter = GetCapturedPaymentCounter(),
            problem = "The same payment can be captured multiple times because callbacks pass the idempotency check concurrently.",
            results
        };
    }

    public async Task<object> DemoPaymentAfterAsync(CancellationToken cancellationToken = default)
    {
        ResetState();

        var tasks = Enumerable.Range(1, ConcurrentActors)
            .Select(callbackNumber => ProcessPaymentAfterAsync(
                DefaultPaymentReference,
                $"callback-{callbackNumber}",
                99.99m,
                cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var capturedCount = results.Count(IsCaptured);

        return new
        {
            mode = "AFTER - Repeated payment callbacks with Redis distributed lock",
            paymentReference = DefaultPaymentReference,
            repeatedCallbacks = ConcurrentActors,
            capturedCount,
            capturedPaymentCounter = GetCapturedPaymentCounter(),
            lockMetrics = _lockService.GetMetrics(),
            explanation = "The Redis lock protects the idempotency section. One callback captures the payment, and duplicates are blocked.",
            results
        };
    }

    public async Task<object> DemoAllAsync(CancellationToken cancellationToken = default)
    {
        var couponBefore = await DemoCouponBeforeAsync(cancellationToken);
        var couponAfter = await DemoCouponAfterAsync(cancellationToken);
        var paymentBefore = await DemoPaymentBeforeAsync(cancellationToken);
        var paymentAfter = await DemoPaymentAfterAsync(cancellationToken);

        return new
        {
            requirement = "Requirement 07 - Distributed Lock outside database locks",
            couponBefore,
            couponAfter,
            paymentBefore,
            paymentAfter,
            metrics = GetMetrics(),
            conclusion = "Redis distributed locks prevent duplicated coupon redemption and duplicated payment capture without relying on database row locks."
        };
    }

    public DistributedLockDemoMetricsSnapshot GetMetrics()
    {
        var useRedis = bool.TryParse(_configuration["Cache:UseRedis"], out var parsedUseRedis) && parsedUseRedis;
        var redisConnectionString = _configuration["Cache:RedisConnectionString"];
        var redisConfigured = useRedis && !string.IsNullOrWhiteSpace(redisConnectionString);

        lock (_stateLock)
        {
            return new DistributedLockDemoMetricsSnapshot
            {
                ProviderName = _lockService.GetMetrics().ProviderName,
                RedisConfigured = redisConfigured,
                CouponBeforeAttempts = Interlocked.Read(ref _couponBeforeAttempts),
                CouponBeforeSuccesses = Interlocked.Read(ref _couponBeforeSuccesses),
                CouponAfterAttempts = Interlocked.Read(ref _couponAfterAttempts),
                CouponAfterSuccesses = Interlocked.Read(ref _couponAfterSuccesses),
                PaymentBeforeAttempts = Interlocked.Read(ref _paymentBeforeAttempts),
                PaymentBeforeCaptures = Interlocked.Read(ref _paymentBeforeCaptures),
                PaymentAfterAttempts = Interlocked.Read(ref _paymentAfterAttempts),
                PaymentAfterCaptures = Interlocked.Read(ref _paymentAfterCaptures),
                ProcessedPaymentReferences = _processedPaymentReferences.Count,
                Coupons = _coupons.Values.Select(ToSnapshot).OrderBy(coupon => coupon.Code).ToList(),
                LockMetrics = _lockService.GetMetrics(),
                LastResetAtUtc = _lastResetAtUtc,
                Explanation = "Distributed locks are applied to coupon redemption and payment idempotency, which are business resources outside database locking."
            };
        }
    }

    private CouponState GetOrCreateCoupon(string couponCode)
    {
        if (_coupons.TryGetValue(couponCode, out var coupon))
        {
            return coupon;
        }

        coupon = new CouponState
        {
            Code = couponCode,
            RemainingRedemptions = DefaultCouponCapacity,
            TotalSuccessfulRedemptions = 0
        };

        _coupons[couponCode] = coupon;
        return coupon;
    }

    private CouponStateSnapshot GetCouponSnapshot(string couponCode)
    {
        lock (_stateLock)
        {
            return ToSnapshot(GetOrCreateCoupon(couponCode));
        }
    }

    private int GetCapturedPaymentCounter()
    {
        lock (_stateLock)
        {
            return _capturedPaymentCounter;
        }
    }

    private static CouponStateSnapshot ToSnapshot(CouponState coupon)
    {
        return new CouponStateSnapshot
        {
            Code = coupon.Code,
            RemainingRedemptions = coupon.RemainingRedemptions,
            TotalSuccessfulRedemptions = coupon.TotalSuccessfulRedemptions
        };
    }

    private static bool IsSuccess(object result)
    {
        var property = result.GetType().GetProperty("success");
        return property?.GetValue(result) is true;
    }

    private static bool IsCaptured(object result)
    {
        var property = result.GetType().GetProperty("captured");
        return property?.GetValue(result) is true;
    }

    private sealed class CouponState
    {
        public string Code { get; set; } = string.Empty;

        public int RemainingRedemptions { get; set; }

        public int TotalSuccessfulRedemptions { get; set; }
    }
}
