namespace ParallelECommerce.Models;

public class SalesRecord
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTime SoldAtUtc { get; set; }

    public decimal Total => Quantity * UnitPrice;
}