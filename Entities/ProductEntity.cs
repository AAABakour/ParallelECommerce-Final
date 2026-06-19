namespace ParallelECommerce.Entities;

public sealed class ProductEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public int PopularityScore { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
