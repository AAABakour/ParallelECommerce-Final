namespace ParallelECommerce.Models;

public class LoadBalancingServerSnapshot
{
    public string Name { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public int Weight { get; set; }
    public int SimulatedProcessingMs { get; set; }
    public int HandledRequests { get; set; }
    public int FailedRequests { get; set; }
    public int ActiveRequests { get; set; }
    public int MaxActiveRequestsObserved { get; set; }
    public double AverageLatencyMs { get; set; }
}
