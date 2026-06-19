namespace ParallelECommerce.Models;

public class StressResourceSummary
{
    public int SampleCount { get; set; }
    public DateTime? FirstSampleAtUtc { get; set; }
    public DateTime? LastSampleAtUtc { get; set; }
    public double PeakCpuUsagePercent { get; set; }
    public double AverageCpuUsagePercent { get; set; }
    public double PeakManagedMemoryMb { get; set; }
    public double PeakWorkingSetMemoryMb { get; set; }
    public int PeakProcessThreadCount { get; set; }
    public int PeakThreadPoolBusyWorkers { get; set; }
    public long TotalHttpRequestsObserved { get; set; }
    public long FailedHttpRequestsObserved { get; set; }
}
