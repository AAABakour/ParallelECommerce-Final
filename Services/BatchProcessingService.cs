using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class BatchProcessingService
{
    private const int DefaultChunkSize = 10;
    private const int DefaultMaxParallelChunks = 4;
    private const int QueueCapacity = 20;

    private readonly Channel<BatchJobRequest> _jobQueue;
    private readonly ConcurrentDictionary<Guid, BatchJobSnapshot> _jobs = new();
    private readonly object _metricsLock = new();

    private int _totalJobsStarted;
    private int _maxQueueDepthObserved;
    private int _maxActiveChunksObserved;
    private int _currentQueueDepth;

    public BatchProcessingService()
    {
        _jobQueue = Channel.CreateBounded<BatchJobRequest>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public List<SalesRecord> GenerateDailySalesRecords(int count)
    {
        var records = new List<SalesRecord>();

        for (var i = 1; i <= count; i++)
        {
            records.Add(new SalesRecord
            {
                Id = i,
                ProductId = (i % 5) + 1,
                Quantity = (i % 3) + 1,
                UnitPrice = 100 + (i % 10) * 25,
                SoldAtUtc = DateTime.UtcNow.Date.AddMinutes(i)
            });
        }

        return records;
    }

    private static int NormalizeTotalRecords(int totalRecords)
    {
        return Math.Clamp(totalRecords, 1, 10_000);
    }

    private static int NormalizeChunkSize(int chunkSize)
    {
        return Math.Clamp(chunkSize, 1, 500);
    }

    private static int NormalizeMaxParallelChunks(int maxParallelChunks)
    {
        return Math.Clamp(maxParallelChunks, 1, 32);
    }

    private async Task<decimal> ProcessSalesRecordAsync(SalesRecord record)
    {
        // Simulates a slow operation such as reading from a database, calculating a report row,
        // or writing an aggregation result.
        await Task.Delay(30);

        return record.Total;
    }

    // BEFORE: sequential processing, record by record. It is intentionally slow.
    public async Task<object> ProcessSequentiallyAsync(int totalRecords)
    {
        totalRecords = NormalizeTotalRecords(totalRecords);
        var records = GenerateDailySalesRecords(totalRecords);

        var stopwatch = Stopwatch.StartNew();

        decimal totalSales = 0;
        var processedRecords = 0;

        foreach (var record in records)
        {
            totalSales += await ProcessSalesRecordAsync(record);
            processedRecords++;
        }

        stopwatch.Stop();

        return new
        {
            mode = "BEFORE - Sequential Batch Processing",
            totalRecords,
            processedRecords,
            totalSales,
            durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            problem = "Records were processed one by one. The whole batch waits for each previous record to finish."
        };
    }

    // AFTER: the dataset is split into chunks, then chunks are processed in parallel with a safe upper limit.
    public async Task<object> ProcessInParallelChunksAsync(int totalRecords, int chunkSize, int maxParallelChunks = DefaultMaxParallelChunks)
    {
        totalRecords = NormalizeTotalRecords(totalRecords);
        chunkSize = NormalizeChunkSize(chunkSize);
        maxParallelChunks = NormalizeMaxParallelChunks(maxParallelChunks);

        var records = GenerateDailySalesRecords(totalRecords);
        var chunks = records.Chunk(chunkSize).Select(chunk => chunk.ToArray()).ToList();
        using var parallelGate = new SemaphoreSlim(maxParallelChunks, maxParallelChunks);

        var stopwatch = Stopwatch.StartNew();
        var activeChunks = 0;
        var maxActiveChunks = 0;

        var chunkTasks = chunks.Select(async (chunk, index) =>
        {
            await parallelGate.WaitAsync();
            var chunkStopwatch = Stopwatch.StartNew();

            try
            {
                var activeNow = Interlocked.Increment(ref activeChunks);
                UpdateMax(ref maxActiveChunks, activeNow);

                decimal chunkTotal = 0;
                var chunkRecords = 0;

                foreach (var record in chunk)
                {
                    chunkTotal += await ProcessSalesRecordAsync(record);
                    chunkRecords++;
                }

                chunkStopwatch.Stop();

                return new BatchChunkSnapshot
                {
                    ChunkNumber = index + 1,
                    RecordsProcessed = chunkRecords,
                    ChunkTotal = chunkTotal,
                    DurationMs = Math.Round(chunkStopwatch.Elapsed.TotalMilliseconds, 2),
                    Status = "Completed"
                };
            }
            finally
            {
                Interlocked.Decrement(ref activeChunks);
                parallelGate.Release();
            }
        });

        var chunkResults = await Task.WhenAll(chunkTasks);
        stopwatch.Stop();

        lock (_metricsLock)
        {
            _maxActiveChunksObserved = Math.Max(_maxActiveChunksObserved, maxActiveChunks);
        }

        return new
        {
            mode = "AFTER - Parallel Chunk Batch Processing",
            totalRecords,
            chunkSize,
            numberOfChunks = chunks.Count,
            maxParallelChunks,
            maxActiveChunksObserved = maxActiveChunks,
            processedRecords = chunkResults.Sum(x => x.RecordsProcessed),
            totalSales = chunkResults.Sum(x => x.ChunkTotal),
            durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            explanation = "Records were split into chunks. Chunks were processed in parallel with a bounded maximum number of active chunks."
        };
    }

    public async Task<BatchJobSnapshot> QueueDailySalesBatchJobAsync(
        int totalRecords = 100,
        int chunkSize = DefaultChunkSize,
        int maxParallelChunks = DefaultMaxParallelChunks)
    {
        totalRecords = NormalizeTotalRecords(totalRecords);
        chunkSize = NormalizeChunkSize(chunkSize);
        maxParallelChunks = NormalizeMaxParallelChunks(maxParallelChunks);

        var job = new BatchJobSnapshot
        {
            JobId = Guid.NewGuid(),
            Mode = "BACKGROUND - Daily Sales Inventory Batch Job",
            Status = "Queued",
            TotalRecords = totalRecords,
            ChunkSize = chunkSize,
            NumberOfChunks = (int)Math.Ceiling(totalRecords / (double)chunkSize),
            MaxParallelChunksConfigured = maxParallelChunks,
            QueuedAtUtc = DateTime.UtcNow
        };

        _jobs[job.JobId] = job;

        Interlocked.Increment(ref _currentQueueDepth);
        UpdateMaxQueueDepth();

        await _jobQueue.Writer.WriteAsync(new BatchJobRequest(
            job.JobId,
            totalRecords,
            chunkSize,
            maxParallelChunks,
            job.Mode));

        return Clone(job);
    }

    public async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _jobQueue.Reader.ReadAllAsync(stoppingToken))
        {
            Interlocked.Decrement(ref _currentQueueDepth);

            if (!_jobs.TryGetValue(request.JobId, out var job))
            {
                continue;
            }

            await ProcessBackgroundJobAsync(job, request, stoppingToken);
        }
    }

    private async Task ProcessBackgroundJobAsync(
        BatchJobSnapshot job,
        BatchJobRequest request,
        CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref _totalJobsStarted);

        job.Status = "Running";
        job.StartedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var records = GenerateDailySalesRecords(request.TotalRecords);
            var chunks = records.Chunk(request.ChunkSize).Select(chunk => chunk.ToArray()).ToList();
            using var parallelGate = new SemaphoreSlim(request.MaxParallelChunks, request.MaxParallelChunks);

            var activeChunks = 0;
            var maxActiveChunks = 0;

            var chunkTasks = chunks.Select(async (chunk, index) =>
            {
                await parallelGate.WaitAsync(stoppingToken);
                var chunkStopwatch = Stopwatch.StartNew();

                try
                {
                    var activeNow = Interlocked.Increment(ref activeChunks);
                    UpdateMax(ref maxActiveChunks, activeNow);

                    decimal chunkTotal = 0;
                    var processed = 0;

                    foreach (var record in chunk)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        chunkTotal += await ProcessSalesRecordAsync(record);
                        processed++;
                    }

                    chunkStopwatch.Stop();

                    return new BatchChunkSnapshot
                    {
                        ChunkNumber = index + 1,
                        RecordsProcessed = processed,
                        ChunkTotal = chunkTotal,
                        DurationMs = Math.Round(chunkStopwatch.Elapsed.TotalMilliseconds, 2),
                        Status = "Completed"
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref activeChunks);
                    parallelGate.Release();
                }
            });

            var chunkResults = await Task.WhenAll(chunkTasks);
            stopwatch.Stop();

            job.Chunks = chunkResults.OrderBy(x => x.ChunkNumber).ToList();
            job.ProcessedRecords = chunkResults.Sum(x => x.RecordsProcessed);
            job.TotalSales = chunkResults.Sum(x => x.ChunkTotal);
            job.MaxActiveChunksObserved = maxActiveChunks;
            job.DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
            job.Status = "Completed";
            job.FinishedAtUtc = DateTime.UtcNow;

            lock (_metricsLock)
            {
                _maxActiveChunksObserved = Math.Max(_maxActiveChunksObserved, maxActiveChunks);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            job.Status = "Failed";
            job.ErrorMessage = ex.Message;
            job.DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
            job.FinishedAtUtc = DateTime.UtcNow;
        }
    }

    public BatchJobSnapshot? GetJob(Guid jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) ? Clone(job) : null;
    }

    public IReadOnlyCollection<BatchJobSnapshot> GetRecentJobs()
    {
        return _jobs.Values
            .OrderByDescending(x => x.QueuedAtUtc)
            .Take(20)
            .Select(Clone)
            .ToList();
    }

    public BatchMetricsSnapshot GetMetrics()
    {
        var jobs = _jobs.Values.ToList();

        return new BatchMetricsSnapshot
        {
            ConfiguredDefaultChunkSize = DefaultChunkSize,
            ConfiguredMaxParallelChunks = DefaultMaxParallelChunks,
            QueuedJobs = jobs.Count(x => x.Status == "Queued"),
            RunningJobs = jobs.Count(x => x.Status == "Running"),
            CompletedJobs = jobs.Count(x => x.Status == "Completed"),
            FailedJobs = jobs.Count(x => x.Status == "Failed"),
            TotalJobsStarted = Volatile.Read(ref _totalJobsStarted),
            MaxQueueDepthObserved = Volatile.Read(ref _maxQueueDepthObserved),
            MaxActiveChunksObserved = Volatile.Read(ref _maxActiveChunksObserved)
        };
    }

    public object Reset()
    {
        _jobs.Clear();
        Interlocked.Exchange(ref _totalJobsStarted, 0);
        Interlocked.Exchange(ref _maxQueueDepthObserved, 0);
        Interlocked.Exchange(ref _maxActiveChunksObserved, 0);
        Interlocked.Exchange(ref _currentQueueDepth, 0);

        return new
        {
            message = "Batch processing metrics were reset.",
            metrics = GetMetrics()
        };
    }

    private void UpdateMaxQueueDepth()
    {
        var currentDepth = Volatile.Read(ref _currentQueueDepth);
        UpdateMax(ref _maxQueueDepthObserved, currentDepth);
    }

    private static void UpdateMax(ref int target, int value)
    {
        int initialValue;
        int computedValue;

        do
        {
            initialValue = Volatile.Read(ref target);
            computedValue = Math.Max(initialValue, value);

            if (computedValue == initialValue)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, computedValue, initialValue) != initialValue);
    }

    private static BatchJobSnapshot Clone(BatchJobSnapshot source)
    {
        return new BatchJobSnapshot
        {
            JobId = source.JobId,
            Mode = source.Mode,
            Status = source.Status,
            TotalRecords = source.TotalRecords,
            ChunkSize = source.ChunkSize,
            NumberOfChunks = source.NumberOfChunks,
            MaxParallelChunksConfigured = source.MaxParallelChunksConfigured,
            MaxActiveChunksObserved = source.MaxActiveChunksObserved,
            ProcessedRecords = source.ProcessedRecords,
            FailedRecords = source.FailedRecords,
            TotalSales = source.TotalSales,
            DurationMs = source.DurationMs,
            QueuedAtUtc = source.QueuedAtUtc,
            StartedAtUtc = source.StartedAtUtc,
            FinishedAtUtc = source.FinishedAtUtc,
            ErrorMessage = source.ErrorMessage,
            Chunks = source.Chunks.Select(chunk => new BatchChunkSnapshot
            {
                ChunkNumber = chunk.ChunkNumber,
                RecordsProcessed = chunk.RecordsProcessed,
                ChunkTotal = chunk.ChunkTotal,
                DurationMs = chunk.DurationMs,
                Status = chunk.Status
            }).ToList()
        };
    }
}
