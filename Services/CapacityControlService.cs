using System.Diagnostics;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class CapacityControlService
{
    public const int MaxParallelOperations = 5;
    private const int DefaultHeavyWorkDurationMs = 500;

    private int _activeOperations;
    private int _queuedOperations;
    private int _maxActiveOperations;
    private int _startedOperations;
    private int _completedOperations;

    // يسمح فقط لـ 5 عمليات ثقيلة أن تعمل بنفس الوقت.
    // العمليات الزائدة لا تُرفض، بل تنتظر حتى تتوفر خانة تنفيذ.
    private readonly SemaphoreSlim _semaphore = new(initialCount: MaxParallelOperations, maxCount: MaxParallelOperations);

    public void ResetMetrics()
    {
        Interlocked.Exchange(ref _activeOperations, 0);
        Interlocked.Exchange(ref _queuedOperations, 0);
        Interlocked.Exchange(ref _maxActiveOperations, 0);
        Interlocked.Exchange(ref _startedOperations, 0);
        Interlocked.Exchange(ref _completedOperations, 0);
    }

    public CapacityMetricsSnapshot GetMetrics()
    {
        return new CapacityMetricsSnapshot
        {
            ConfiguredMaxParallelOperations = MaxParallelOperations,
            ActiveOperations = Interlocked.CompareExchange(ref _activeOperations, 0, 0),
            QueuedOperations = Interlocked.CompareExchange(ref _queuedOperations, 0, 0),
            MaxActiveOperationsObserved = Interlocked.CompareExchange(ref _maxActiveOperations, 0, 0),
            StartedOperations = Interlocked.CompareExchange(ref _startedOperations, 0, 0),
            CompletedOperations = Interlocked.CompareExchange(ref _completedOperations, 0, 0)
        };
    }

    public int GetMaxActiveOperations()
    {
        return Interlocked.CompareExchange(ref _maxActiveOperations, 0, 0);
    }

    // BEFORE: لا يوجد أي تحكم بعدد العمليات المتوازية.
    // هذا يمثل الحالة الخطرة: كل الطلبات الثقيلة تدخل مباشرة وتستهلك الموارد معاً.
    public Task<CapacityOperationResult> RunWithoutCapacityControlAsync(int workDurationMs = DefaultHeavyWorkDurationMs)
    {
        return RunHeavyOperationAsync(
            mode: "BEFORE - No Capacity Control",
            waitBeforeStartMs: 0,
            queuedOperationsWhenStarted: 0,
            explanation: "No capacity limit was applied; every request was allowed to run immediately.",
            workDurationMs: workDurationMs);
    }

    // AFTER: التحكم بعدد العمليات المتوازية باستخدام SemaphoreSlim.
    // هذا يمثل الحل: لا يعمل أكثر من MaxParallelOperations عمليات ثقيلة في نفس اللحظة.
    public async Task<CapacityOperationResult> RunWithCapacityControlAsync(int workDurationMs = DefaultHeavyWorkDurationMs)
    {
        var queuedAfterIncrement = Interlocked.Increment(ref _queuedOperations);
        var waitStopwatch = Stopwatch.StartNew();

        await _semaphore.WaitAsync();

        waitStopwatch.Stop();
        Interlocked.Decrement(ref _queuedOperations);

        try
        {
            return await RunHeavyOperationAsync(
                mode: "AFTER - Protected with SemaphoreSlim",
                waitBeforeStartMs: waitStopwatch.Elapsed.TotalMilliseconds,
                queuedOperationsWhenStarted: queuedAfterIncrement,
                explanation: $"SemaphoreSlim allowed only {MaxParallelOperations} heavy operations to run at the same time; extra requests waited instead of overloading the system.",
                workDurationMs: workDurationMs);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<CapacityOperationResult> RunHeavyOperationAsync(
        string mode,
        double waitBeforeStartMs,
        int queuedOperationsWhenStarted,
        string explanation,
        int workDurationMs)
    {
        var workStopwatch = Stopwatch.StartNew();
        var currentActive = Interlocked.Increment(ref _activeOperations);
        Interlocked.Increment(ref _startedOperations);
        UpdateMaxActiveOperations(currentActive);

        try
        {
            // محاكاة عملية ثقيلة مثل Payment Gateway أو DB query أو External API.
            await Task.Delay(workDurationMs);

            return new CapacityOperationResult
            {
                Mode = mode,
                ConfiguredMaxParallelOperations = MaxParallelOperations,
                ActiveOperationsWhenStarted = currentActive,
                QueuedOperationsWhenStarted = queuedOperationsWhenStarted,
                MaxActiveOperationsObserved = GetMaxActiveOperations(),
                WaitBeforeStartMs = Math.Round(waitBeforeStartMs, 2),
                WorkDurationMs = Math.Round(workStopwatch.Elapsed.TotalMilliseconds, 2),
                Explanation = explanation
            };
        }
        finally
        {
            workStopwatch.Stop();
            Interlocked.Increment(ref _completedOperations);
            Interlocked.Decrement(ref _activeOperations);
        }
    }

    private void UpdateMaxActiveOperations(int currentActive)
    {
        int previousMax;
        do
        {
            previousMax = Interlocked.CompareExchange(ref _maxActiveOperations, 0, 0);

            if (currentActive <= previousMax)
            {
                break;
            }
        }
        while (Interlocked.CompareExchange(ref _maxActiveOperations, currentActive, previousMax) != previousMax);
    }
}
