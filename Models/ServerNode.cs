namespace ParallelECommerce.Models;

public class ServerNode
{
    public string Name { get; set; } = string.Empty;
    public bool IsHealthy { get; set; } = true;
    public int Weight { get; set; } = 1;
    public int SimulatedProcessingMs { get; set; } = 50;
    public int HandledRequests { get; set; }
    public int FailedRequests { get; set; }
    public int ActiveRequests { get; set; }
    public int MaxActiveRequestsObserved { get; set; }
    public double TotalLatencyMs { get; set; }
}
