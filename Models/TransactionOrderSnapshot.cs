namespace ParallelECommerce.Models;

public class TransactionOrderSnapshot
{
    public int OrderId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public string CustomerEmail { get; set; } = string.Empty;

    public string PaymentReference { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Status { get; set; } = "Created";

    public DateTime CreatedAtUtc { get; set; }
}
