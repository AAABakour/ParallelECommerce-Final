namespace ParallelECommerce.Models;

public class BatchChunkSnapshot
{
    public int ChunkNumber { get; set; }
    public int RecordsProcessed { get; set; }
    public decimal ChunkTotal { get; set; }
    public double DurationMs { get; set; }
    public string Status { get; set; } = "Completed";
}
