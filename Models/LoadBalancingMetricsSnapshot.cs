namespace ParallelECommerce.Models;

public class LoadBalancingMetricsSnapshot
{
    public string SelectedStrategy { get; set; } = string.Empty;
    public int TotalRequests { get; set; }
    public int FailedRequests { get; set; }
    public int ActiveRequests { get; set; }
    public int HealthyServers { get; set; }
    public int UnhealthyServers { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<LoadBalancingServerSnapshot> Servers { get; set; } = new();
}
