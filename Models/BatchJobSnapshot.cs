namespace ParallelECommerce.Models;

public class BatchJobSnapshot
{
    public Guid JobId { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public int TotalRecords { get; set; }
    public int ChunkSize { get; set; }
    public int NumberOfChunks { get; set; }
    public int MaxParallelChunksConfigured { get; set; }
    public int MaxActiveChunksObserved { get; set; }
    public int ProcessedRecords { get; set; }
    public int FailedRecords { get; set; }
    public decimal TotalSales { get; set; }
    public double DurationMs { get; set; }
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public List<BatchChunkSnapshot> Chunks { get; set; } = new();
}
