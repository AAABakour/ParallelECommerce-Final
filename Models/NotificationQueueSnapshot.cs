namespace ParallelECommerce.Models;

public class NotificationQueueSnapshot
{
    public int ConfiguredQueueCapacity { get; set; }
    public int QueuedJobs { get; set; }
    public int ProcessedJobs { get; set; }
    public int FailedJobs { get; set; }
    public int CurrentQueueDepth { get; set; }
    public int ActiveBackgroundWorkers { get; set; }
    public int MaxQueueDepthObserved { get; set; }
    public int MaxActiveBackgroundWorkersObserved { get; set; }
    public DateTime? LastEnqueuedAtUtc { get; set; }
    public DateTime? LastProcessedAtUtc { get; set; }
    public DateTime? LastFailedAtUtc { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
