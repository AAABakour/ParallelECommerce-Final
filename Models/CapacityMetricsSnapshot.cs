namespace ParallelECommerce.Models;

public class CapacityMetricsSnapshot
{
    public int ConfiguredMaxParallelOperations { get; set; }
    public int ActiveOperations { get; set; }
    public int QueuedOperations { get; set; }
    public int MaxActiveOperationsObserved { get; set; }
    public int StartedOperations { get; set; }
    public int CompletedOperations { get; set; }
}
