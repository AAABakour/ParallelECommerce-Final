namespace ParallelECommerce.Models;

public class EndpointMetricSnapshot
{
    public string Endpoint { get; set; } = string.Empty;
    public long TotalRequests { get; set; }
    public long FailedRequests { get; set; }
    public double AverageDurationMs { get; set; }
    public double MaxDurationMs { get; set; }
    public int LastStatusCode { get; set; }
    public DateTime LastRequestAtUtc { get; set; }
}
