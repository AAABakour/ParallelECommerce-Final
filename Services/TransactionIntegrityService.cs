using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class TransactionIntegrityService
{
    private const int DemoProductId = 1;
    private const int InitialDemoStock = 5;
    private const int ConcurrentUsers = 10;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _transactionGate = new(1, 1);
    private readonly List<TransactionOrderSnapshot> _orders = new();
    private readonly Dictionary<string, PaymentCaptureSnapshot> _capturedPayments = new(StringComparer.OrdinalIgnoreCase);

    private int _currentStock = InitialDemoStock;
    private int _nextOrderId = 1;
    private DateTime _lastResetAtUtc = DateTime.UtcNow;

    private long _beforeAttempts;
    private long _beforePaymentsCaptured;
    private long _beforeInventoryDeductions;
    private long _beforeOrdersCreated;
    private long _beforePartialFailures;

    private long _afterAttempts;
    private long _afterCommits;
    private long _afterRollbacks;
    private long _afterPaymentsCaptured;
    private long _afterInventoryDeductions;
    private long _afterOrdersCreated;
    private long _afterOutOfStockFailures;

    public object ResetState()
    {
        lock (_stateLock)
        {
            _currentStock = InitialDemoStock;
            _nextOrderId = 1;
            _orders.Clear();
            _capturedPayments.Clear();
        }

        Interlocked.Exchange(ref _beforeAttempts, 0);
        Interlocked.Exchange(ref _beforePaymentsCaptured, 0);
        Interlocked.Exchange(ref _beforeInventoryDeductions, 0);
        Interlocked.Exchange(ref _beforeOrdersCreated, 0);
        Interlocked.Exchange(ref _beforePartialFailures, 0);

        Interlocked.Exchange(ref _afterAttempts, 0);
        Interlocked.Exchange(ref _afterCommits, 0);
        Interlocked.Exchange(ref _afterRollbacks, 0);
        Interlocked.Exchange(ref _afterPaymentsCaptured, 0);
        Interlocked.Exchange(ref _afterInventoryDeductions, 0);
        Interlocked.Exchange(ref _afterOrdersCreated, 0);
        Interlocked.Exchange(ref _afterOutOfStockFailures, 0);

        _lastResetAtUtc = DateTime.UtcNow;

        return new
        {
            message = "Transaction-integrity demo state and metrics were reset.",
            metrics = GetMetrics()
        };
    }

    public TransactionMetricsSnapshot GetMetrics()
    {
        lock (_stateLock)
        {
            return new TransactionMetricsSnapshot
            {
                ProviderName = "Application transaction coordinator with rollback log and serialized commit section",
                InitialStock = InitialDemoStock,
                CurrentStock = _currentStock,
                OrdersCreated = _orders.Count,
                CapturedPayments = _capturedPayments.Count,
                InconsistentPartialOperations = CalculateInconsistentPartialOperations(),
                BeforeAttempts = Interlocked.Read(ref _beforeAttempts),
                BeforePaymentsCaptured = Interlocked.Read(ref _beforePaymentsCaptured),
                BeforeInventoryDeductions = Interlocked.Read(ref _beforeInventoryDeductions),
                BeforeOrdersCreated = Interlocked.Read(ref _beforeOrdersCreated),
                BeforePartialFailures = Interlocked.Read(ref _beforePartialFailures),
                AfterAttempts = Interlocked.Read(ref _afterAttempts),
                AfterCommits = Interlocked.Read(ref _afterCommits),
                AfterRollbacks = Interlocked.Read(ref _afterRollbacks),
                AfterPaymentsCaptured = Interlocked.Read(ref _afterPaymentsCaptured),
                AfterInventoryDeductions = Interlocked.Read(ref _afterInventoryDeductions),
                AfterOrdersCreated = Interlocked.Read(ref _afterOrdersCreated),
                AfterOutOfStockFailures = Interlocked.Read(ref _afterOutOfStockFailures),
                LastResetAtUtc = _lastResetAtUtc,
                Explanation = "A valid completed checkout must have exactly one order, one captured payment, and one inventory deduction. Before mode leaves partial side effects. After mode commits all steps or rolls all side effects back."
            };
        }
    }

    public async Task<object> CheckoutBeforeAsync(
        int productId,
        int quantity,
        string customerEmail,
        string paymentReference,
        decimal amount,
        bool simulateFailureAfterPayment,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _beforeAttempts);

        if (productId != DemoProductId || quantity <= 0)
        {
            return new
            {
                mode = "BEFORE - Checkout without transaction boundary",
                success = false,
                reason = "Invalid product or quantity."
            };
        }

        var inventoryDeducted = false;
        var paymentCaptured = false;

        lock (_stateLock)
        {
            if (_currentStock < quantity)
            {
                return new
                {
                    mode = "BEFORE - Checkout without transaction boundary",
                    success = false,
                    reason = "Not enough stock.",
                    metrics = GetMetricsUnsafe()
                };
            }

            _currentStock -= quantity;
            inventoryDeducted = true;
        }

        Interlocked.Increment(ref _beforeInventoryDeductions);

        await Task.Delay(30, cancellationToken);

        lock (_stateLock)
        {
            _capturedPayments[paymentReference] = new PaymentCaptureSnapshot
            {
                PaymentReference = paymentReference,
                Amount = amount,
                CapturedAtUtc = DateTime.UtcNow
            };
            paymentCaptured = true;
        }

        Interlocked.Increment(ref _beforePaymentsCaptured);

        await Task.Delay(30, cancellationToken);

        if (simulateFailureAfterPayment)
        {
            Interlocked.Increment(ref _beforePartialFailures);

            return new
            {
                mode = "BEFORE - Checkout without transaction boundary",
                success = false,
                productId,
                quantity,
                paymentReference,
                inventoryDeducted,
                paymentCaptured,
                orderCreated = false,
                problem = "A failure happened after payment and inventory update. Without a transaction, these side effects remain although the order was not created.",
                metrics = GetMetrics()
            };
        }

        TransactionOrderSnapshot order;
        lock (_stateLock)
        {
            order = CreateOrderUnsafe(productId, quantity, customerEmail, paymentReference, amount);
        }

        Interlocked.Increment(ref _beforeOrdersCreated);

        return new
        {
            mode = "BEFORE - Checkout without transaction boundary",
            success = true,
            productId,
            quantity,
            paymentReference,
            inventoryDeducted,
            paymentCaptured,
            orderCreated = true,
            order,
            metrics = GetMetrics()
        };
    }

    public async Task<object> CheckoutAfterAsync(
        int productId,
        int quantity,
        string customerEmail,
        string paymentReference,
        decimal amount,
        bool simulateFailureAfterPayment,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _afterAttempts);

        if (productId != DemoProductId || quantity <= 0)
        {
            return new
            {
                mode = "AFTER - Checkout inside atomic transaction boundary",
                success = false,
                reason = "Invalid product or quantity."
            };
        }

        await _transactionGate.WaitAsync(cancellationToken);
        try
        {
            var inventoryDeducted = false;
            var paymentCaptured = false;
            TransactionOrderSnapshot? createdOrder = null;

            try
            {
                lock (_stateLock)
                {
                    if (_currentStock < quantity)
                    {
                        Interlocked.Increment(ref _afterOutOfStockFailures);

                        return new
                        {
                            mode = "AFTER - Checkout inside atomic transaction boundary",
                            success = false,
                            reason = "Not enough stock. No payment was captured and no order was created.",
                            metrics = GetMetricsUnsafe()
                        };
                    }

                    _currentStock -= quantity;
                    inventoryDeducted = true;

                    _capturedPayments[paymentReference] = new PaymentCaptureSnapshot
                    {
                        PaymentReference = paymentReference,
                        Amount = amount,
                        CapturedAtUtc = DateTime.UtcNow
                    };
                    paymentCaptured = true;
                }

                await Task.Delay(20, cancellationToken);

                if (simulateFailureAfterPayment)
                {
                    throw new InvalidOperationException("Simulated failure after payment capture and inventory deduction.");
                }

                lock (_stateLock)
                {
                    createdOrder = CreateOrderUnsafe(productId, quantity, customerEmail, paymentReference, amount);
                }

                Interlocked.Increment(ref _afterInventoryDeductions);
                Interlocked.Increment(ref _afterPaymentsCaptured);
                Interlocked.Increment(ref _afterOrdersCreated);
                Interlocked.Increment(ref _afterCommits);

                return new
                {
                    mode = "AFTER - Checkout inside atomic transaction boundary",
                    success = true,
                    committed = true,
                    rolledBack = false,
                    productId,
                    quantity,
                    paymentReference,
                    inventoryDeducted,
                    paymentCaptured,
                    orderCreated = true,
                    order = createdOrder,
                    explanation = "All steps committed together: payment captured, inventory updated, and order created.",
                    metrics = GetMetrics()
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
            {
                lock (_stateLock)
                {
                    if (createdOrder is not null)
                    {
                        _orders.RemoveAll(order => order.OrderId == createdOrder.OrderId);
                    }

                    if (paymentCaptured)
                    {
                        _capturedPayments.Remove(paymentReference);
                    }

                    if (inventoryDeducted)
                    {
                        _currentStock += quantity;
                    }
                }

                Interlocked.Increment(ref _afterRollbacks);

                return new
                {
                    mode = "AFTER - Checkout inside atomic transaction boundary",
                    success = false,
                    committed = false,
                    rolledBack = true,
                    productId,
                    quantity,
                    paymentReference,
                    inventoryDeductedWasRolledBack = inventoryDeducted,
                    paymentCaptureWasRolledBack = paymentCaptured,
                    orderCreated = false,
                    reason = ex.Message,
                    explanation = "The transaction restored every side effect, so there is no captured payment without an order and no lost stock.",
                    metrics = GetMetrics()
                };
            }
        }
        finally
        {
            _transactionGate.Release();
        }
    }

    public async Task<object> DemoFailureBeforeAsync(CancellationToken cancellationToken = default)
    {
        ResetState();

        var tasks = Enumerable.Range(1, ConcurrentUsers)
            .Select(userNumber => CheckoutBeforeAsync(
                DemoProductId,
                1,
                $"user{userNumber}@example.com",
                $"payment-req08-fail-before-{userNumber}",
                1750m,
                simulateFailureAfterPayment: true,
                cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var metrics = GetMetrics();

        return new
        {
            mode = "BEFORE - Composite checkout failure without transaction",
            initialStock = InitialDemoStock,
            concurrentUsers = ConcurrentUsers,
            simulatedFailureAfterPayment = true,
            finalStock = metrics.CurrentStock,
            capturedPayments = metrics.CapturedPayments,
            ordersCreated = metrics.OrdersCreated,
            inconsistentPartialOperations = metrics.InconsistentPartialOperations,
            problem = "Some requests captured payment and deducted stock, then failed before creating the order. Because there is no transaction rollback, the system remains inconsistent.",
            results,
            metrics
        };
    }

    public async Task<object> DemoFailureAfterAsync(CancellationToken cancellationToken = default)
    {
        ResetState();

        var tasks = Enumerable.Range(1, ConcurrentUsers)
            .Select(userNumber => CheckoutAfterAsync(
                DemoProductId,
                1,
                $"user{userNumber}@example.com",
                $"payment-req08-fail-after-{userNumber}",
                1750m,
                simulateFailureAfterPayment: true,
                cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var metrics = GetMetrics();

        return new
        {
            mode = "AFTER - Composite checkout failure with transaction rollback",
            initialStock = InitialDemoStock,
            concurrentUsers = ConcurrentUsers,
            simulatedFailureAfterPayment = true,
            finalStock = metrics.CurrentStock,
            capturedPayments = metrics.CapturedPayments,
            ordersCreated = metrics.OrdersCreated,
            rollbacks = metrics.AfterRollbacks,
            inconsistentPartialOperations = metrics.InconsistentPartialOperations,
            explanation = "Every failed checkout was rolled back. Stock returned to its original value and no payment remains without an order.",
            results,
            metrics
        };
    }

    public async Task<object> DemoSuccessfulConcurrentAfterAsync(CancellationToken cancellationToken = default)
    {
        ResetState();

        var tasks = Enumerable.Range(1, ConcurrentUsers)
            .Select(userNumber => CheckoutAfterAsync(
                DemoProductId,
                1,
                $"user{userNumber}@example.com",
                $"payment-req08-success-after-{userNumber}",
                1750m,
                simulateFailureAfterPayment: false,
                cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var metrics = GetMetrics();

        return new
        {
            mode = "AFTER - Concurrent successful checkout with ACID-like transaction boundary",
            initialStock = InitialDemoStock,
            concurrentUsers = ConcurrentUsers,
            committedOrders = metrics.AfterCommits,
            outOfStockFailures = metrics.AfterOutOfStockFailures,
            finalStock = metrics.CurrentStock,
            capturedPayments = metrics.CapturedPayments,
            ordersCreated = metrics.OrdersCreated,
            invariant = "ordersCreated == capturedPayments and finalStock == initialStock - committedOrders",
            invariantHolds = metrics.OrdersCreated == metrics.CapturedPayments && metrics.CurrentStock == InitialDemoStock - metrics.OrdersCreated,
            results,
            metrics
        };
    }

    public async Task<object> DemoAllAsync(CancellationToken cancellationToken = default)
    {
        var before = await DemoFailureBeforeAsync(cancellationToken);
        var afterRollback = await DemoFailureAfterAsync(cancellationToken);
        var afterCommit = await DemoSuccessfulConcurrentAfterAsync(cancellationToken);

        return new
        {
            requirement = "Requirement 08 - ACID / Transaction Integrity",
            before,
            afterRollback,
            afterCommit
        };
    }

    private TransactionOrderSnapshot CreateOrderUnsafe(
        int productId,
        int quantity,
        string customerEmail,
        string paymentReference,
        decimal amount)
    {
        var order = new TransactionOrderSnapshot
        {
            OrderId = _nextOrderId++,
            ProductId = productId,
            Quantity = quantity,
            CustomerEmail = customerEmail,
            PaymentReference = paymentReference,
            Amount = amount,
            Status = "Created",
            CreatedAtUtc = DateTime.UtcNow
        };

        _orders.Add(order);
        return CloneOrder(order);
    }

    private int CalculateInconsistentPartialOperations()
    {
        var paymentReferencesWithOrders = _orders
            .Select(order => order.PaymentReference)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _capturedPayments.Keys.Count(paymentReference => !paymentReferencesWithOrders.Contains(paymentReference));
    }

    private TransactionMetricsSnapshot GetMetricsUnsafe()
    {
        return new TransactionMetricsSnapshot
        {
            ProviderName = "Application transaction coordinator with rollback log and serialized commit section",
            InitialStock = InitialDemoStock,
            CurrentStock = _currentStock,
            OrdersCreated = _orders.Count,
            CapturedPayments = _capturedPayments.Count,
            InconsistentPartialOperations = CalculateInconsistentPartialOperations(),
            BeforeAttempts = Interlocked.Read(ref _beforeAttempts),
            BeforePaymentsCaptured = Interlocked.Read(ref _beforePaymentsCaptured),
            BeforeInventoryDeductions = Interlocked.Read(ref _beforeInventoryDeductions),
            BeforeOrdersCreated = Interlocked.Read(ref _beforeOrdersCreated),
            BeforePartialFailures = Interlocked.Read(ref _beforePartialFailures),
            AfterAttempts = Interlocked.Read(ref _afterAttempts),
            AfterCommits = Interlocked.Read(ref _afterCommits),
            AfterRollbacks = Interlocked.Read(ref _afterRollbacks),
            AfterPaymentsCaptured = Interlocked.Read(ref _afterPaymentsCaptured),
            AfterInventoryDeductions = Interlocked.Read(ref _afterInventoryDeductions),
            AfterOrdersCreated = Interlocked.Read(ref _afterOrdersCreated),
            AfterOutOfStockFailures = Interlocked.Read(ref _afterOutOfStockFailures),
            LastResetAtUtc = _lastResetAtUtc,
            Explanation = "A valid completed checkout must have exactly one order, one captured payment, and one inventory deduction. Before mode leaves partial side effects. After mode commits all steps or rolls all side effects back."
        };
    }

    private static TransactionOrderSnapshot CloneOrder(TransactionOrderSnapshot order)
    {
        return new TransactionOrderSnapshot
        {
            OrderId = order.OrderId,
            ProductId = order.ProductId,
            Quantity = order.Quantity,
            CustomerEmail = order.CustomerEmail,
            PaymentReference = order.PaymentReference,
            Amount = order.Amount,
            Status = order.Status,
            CreatedAtUtc = order.CreatedAtUtc
        };
    }
}
