namespace ParallelECommerce.Models;

public class BatchMetricsSnapshot
{
    public int ConfiguredDefaultChunkSize { get; set; }
    public int ConfiguredMaxParallelChunks { get; set; }
    public int QueuedJobs { get; set; }
    public int RunningJobs { get; set; }
    public int CompletedJobs { get; set; }
    public int FailedJobs { get; set; }
    public int TotalJobsStarted { get; set; }
    public int MaxQueueDepthObserved { get; set; }
    public int MaxActiveChunksObserved { get; set; }
}
