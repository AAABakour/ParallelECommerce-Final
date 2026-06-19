namespace ParallelECommerce.Entities;

public sealed class PaymentEntity
{
    public int Id { get; set; }

    public string PaymentReference { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CapturedAtUtc { get; set; }
}
