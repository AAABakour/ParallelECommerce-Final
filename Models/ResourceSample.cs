namespace ParallelECommerce.Models;

public class ResourceSample
{
    public DateTime TimestampUtc { get; set; }
    public double CpuUsagePercent { get; set; }
    public double ManagedMemoryMb { get; set; }
    public double WorkingSetMemoryMb { get; set; }
    public int ProcessThreadCount { get; set; }
    public int ThreadPoolBusyWorkers { get; set; }
    public int ThreadPoolAvailableWorkers { get; set; }
    public int ThreadPoolMaxWorkers { get; set; }
    public long ActiveHttpRequests { get; set; }
    public long TotalHttpRequests { get; set; }
    public long FailedHttpRequests { get; set; }
    public double AverageHttpLatencyMs { get; set; }
}
