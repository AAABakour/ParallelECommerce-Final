namespace ParallelECommerce.Entities;

public sealed class OrderEntity
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public string CustomerEmail { get; set; } = string.Empty;

    public string PaymentReference { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
