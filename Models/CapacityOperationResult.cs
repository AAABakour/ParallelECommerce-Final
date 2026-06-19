namespace ParallelECommerce.Models;

public class CapacityOperationResult
{
    public string Mode { get; set; } = string.Empty;
    public int ConfiguredMaxParallelOperations { get; set; }
    public int ActiveOperationsWhenStarted { get; set; }
    public int QueuedOperationsWhenStarted { get; set; }
    public int MaxActiveOperationsObserved { get; set; }
    public double WaitBeforeStartMs { get; set; }
    public double WorkDurationMs { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
