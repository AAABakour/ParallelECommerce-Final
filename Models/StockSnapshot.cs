namespace ParallelECommerce.Models;

public class StockSnapshot
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public DateTime SnapshotAtUtc { get; set; } = DateTime.UtcNow;
}
