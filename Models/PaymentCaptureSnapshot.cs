namespace ParallelECommerce.Models;

public class PaymentCaptureSnapshot
{
    public string PaymentReference { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime CapturedAtUtc { get; set; }
}
