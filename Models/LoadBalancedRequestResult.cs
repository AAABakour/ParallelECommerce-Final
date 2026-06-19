namespace ParallelECommerce.Models;

public class LoadBalancedRequestResult
{
    public string Mode { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public int RequestId { get; set; }
    public string SelectedServer { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public double LatencyMs { get; set; }
    public string Message { get; set; } = string.Empty;
}
