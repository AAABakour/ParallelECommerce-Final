namespace ParallelECommerce.Models;

public record BatchJobRequest(
    Guid JobId,
    int TotalRecords,
    int ChunkSize,
    int MaxParallelChunks,
    string Mode);
