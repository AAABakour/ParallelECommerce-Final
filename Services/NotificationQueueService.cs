using System.Threading.Channels;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class NotificationQueueService
{
    private const int QueueCapacity = 200;

    private readonly Channel<NotificationJob> _channel = Channel.CreateBounded<NotificationJob>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

    private int _queuedJobs;
    private int _processedJobs;
    private int _failedJobs;
    private int _currentQueueDepth;
    private int _activeBackgroundWorkers;
    private int _maxQueueDepthObserved;
    private int _maxActiveBackgroundWorkersObserved;

    private DateTime? _lastEnqueuedAtUtc;
    private DateTime? _lastProcessedAtUtc;
    private DateTime? _lastFailedAtUtc;

    public async Task EnqueueAsync(NotificationJob job, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(job, cancellationToken);

        Interlocked.Increment(ref _queuedJobs);
        var depth = Interlocked.Increment(ref _currentQueueDepth);
        UpdateMax(ref _maxQueueDepthObserved, depth);

        _lastEnqueuedAtUtc = DateTime.UtcNow;
    }

    public async Task<NotificationJob> DequeueAsync(CancellationToken cancellationToken)
    {
        var job = await _channel.Reader.ReadAsync(cancellationToken);

        Interlocked.Decrement(ref _currentQueueDepth);
        var active = Interlocked.Increment(ref _activeBackgroundWorkers);
        UpdateMax(ref _maxActiveBackgroundWorkersObserved, active);

        return job;
    }

    public void MarkAsProcessed()
    {
        Interlocked.Increment(ref _processedJobs);
        Interlocked.Decrement(ref _activeBackgroundWorkers);
        _lastProcessedAtUtc = DateTime.UtcNow;
    }

    public void MarkAsFailed()
    {
        Interlocked.Increment(ref _failedJobs);
        Interlocked.Decrement(ref _activeBackgroundWorkers);
        _lastFailedAtUtc = DateTime.UtcNow;
    }

    public NotificationQueueSnapshot GetStatus()
    {
        return new NotificationQueueSnapshot
        {
            ConfiguredQueueCapacity = QueueCapacity,
            QueuedJobs = Volatile.Read(ref _queuedJobs),
            ProcessedJobs = Volatile.Read(ref _processedJobs),
            FailedJobs = Volatile.Read(ref _failedJobs),
            CurrentQueueDepth = Math.Max(0, Volatile.Read(ref _currentQueueDepth)),
            ActiveBackgroundWorkers = Math.Max(0, Volatile.Read(ref _activeBackgroundWorkers)),
            MaxQueueDepthObserved = Volatile.Read(ref _maxQueueDepthObserved),
            MaxActiveBackgroundWorkersObserved = Volatile.Read(ref _maxActiveBackgroundWorkersObserved),
            LastEnqueuedAtUtc = _lastEnqueuedAtUtc,
            LastProcessedAtUtc = _lastProcessedAtUtc,
            LastFailedAtUtc = _lastFailedAtUtc,
            Explanation = "Queued jobs are accepted quickly by the HTTP request and processed later by the background worker."
        };
    }

    public NotificationQueueSnapshot ResetMetrics()
    {
        // Drain any queued jobs from previous manual tests so each measurement starts clean.
        while (_channel.Reader.TryRead(out _))
        {
            // no-op
        }

        Interlocked.Exchange(ref _queuedJobs, 0);
        Interlocked.Exchange(ref _processedJobs, 0);
        Interlocked.Exchange(ref _failedJobs, 0);
        Interlocked.Exchange(ref _currentQueueDepth, 0);
        Interlocked.Exchange(ref _activeBackgroundWorkers, 0);
        Interlocked.Exchange(ref _maxQueueDepthObserved, 0);
        Interlocked.Exchange(ref _maxActiveBackgroundWorkersObserved, 0);

        _lastEnqueuedAtUtc = null;
        _lastProcessedAtUtc = null;
        _lastFailedAtUtc = null;

        return GetStatus();
    }

    private static void UpdateMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}
