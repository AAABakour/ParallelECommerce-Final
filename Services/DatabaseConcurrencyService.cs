using Microsoft.EntityFrameworkCore;
using ParallelECommerce.Data;
using ParallelECommerce.Entities;

namespace ParallelECommerce.Services;

public sealed class DatabaseConcurrencyService(IDbContextFactory<ParallelECommerceDbContext> dbFactory)
{
    private const int ProductId = 1;
    private const int InitialStock = 5;
    private const int ConcurrentUsers = 10;
    private const int PurchaseQuantity = 1;
    private const int MaxConcurrencyRetries = 5;

    public async Task<object> ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        var product = await dbContext.Products
            .SingleOrDefaultAsync(product => product.Id == ProductId, cancellationToken);

        if (product is null)
        {
            product = new ProductEntity
            {
                Id = ProductId,
                Name = "Demo Laptop",
                Price = 1750.00m,
                StockQuantity = InitialStock,
                PopularityScore = 95
            };

            dbContext.Products.Add(product);
        }
        else
        {
            product.StockQuantity = InitialStock;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new
        {
            requirement = "Requirement 07 - Real DB Optimistic Locking",
            message = "Database concurrency demo stock was reset.",
            productId = ProductId,
            stockQuantity = product.StockQuantity,
            rowVersion = Convert.ToBase64String(product.RowVersion)
        };
    }

    public async Task<object> RunOptimisticStockDemoAsync(CancellationToken cancellationToken = default)
    {
        await ResetAsync(cancellationToken);

        var allFirstReadsComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReadCount = 0;

        void MarkFirstReadReady()
        {
            if (Interlocked.Increment(ref firstReadCount) == ConcurrentUsers)
            {
                allFirstReadsComplete.TrySetResult();
            }
        }

        var tasks = Enumerable.Range(1, ConcurrentUsers)
            .Select(userNumber => TryPurchaseAsync(
                userNumber,
                MarkFirstReadReady,
                allFirstReadsComplete.Task,
                cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(result => result.Status == PurchaseStatus.Success);
        var outOfStockCount = results.Count(result => result.Status == PurchaseStatus.OutOfStock);
        var conflictCount = results.Sum(result => result.ConflictCount);
        var finalStock = await GetCurrentStockAsync(cancellationToken);

        return new
        {
            requirement = "Requirement 07 - Real DB Optimistic Locking",
            initialStock = InitialStock,
            concurrentUsers = ConcurrentUsers,
            successCount,
            outOfStockCount,
            conflictCount,
            finalStock,
            invariantHolds =
                successCount <= InitialStock &&
                finalStock == InitialStock - successCount &&
                finalStock >= 0,
            perUserResults = results
                .OrderBy(result => result.UserNumber)
                .Select(result => new
                {
                    userNumber = result.UserNumber,
                    status = result.Status,
                    attempts = result.Attempts,
                    conflictCount = result.ConflictCount,
                    remainingStockSeen = result.RemainingStockSeen,
                    message = result.Message
                })
        };
    }

    private async Task<PurchaseAttemptResult> TryPurchaseAsync(
        int userNumber,
        Action markFirstReadReady,
        Task allFirstReadsComplete,
        CancellationToken cancellationToken)
    {
        var conflictCount = 0;

        for (var attempt = 1; attempt <= MaxConcurrencyRetries + 1; attempt++)
        {
            await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);

            var product = await dbContext.Products
                .SingleAsync(product => product.Id == ProductId, cancellationToken);

            if (attempt == 1)
            {
                markFirstReadReady();
                await allFirstReadsComplete.WaitAsync(cancellationToken);
            }

            if (product.StockQuantity < PurchaseQuantity)
            {
                return new PurchaseAttemptResult(
                    userNumber,
                    PurchaseStatus.OutOfStock,
                    attempt,
                    conflictCount,
                    product.StockQuantity,
                    "Stock was already exhausted after retrying with the latest database row version.");
            }

            product.StockQuantity -= PurchaseQuantity;

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return new PurchaseAttemptResult(
                    userNumber,
                    PurchaseStatus.Success,
                    attempt,
                    conflictCount,
                    product.StockQuantity,
                    "Purchase committed with EF Core optimistic concurrency.");
            }
            catch (DbUpdateConcurrencyException)
            {
                conflictCount++;

                if (attempt == MaxConcurrencyRetries + 1)
                {
                    return new PurchaseAttemptResult(
                        userNumber,
                        PurchaseStatus.RetryLimitReached,
                        attempt,
                        conflictCount,
                        null,
                        "Concurrency retry limit was reached before the purchase could commit.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }

        return new PurchaseAttemptResult(
            userNumber,
            PurchaseStatus.RetryLimitReached,
            MaxConcurrencyRetries + 1,
            conflictCount,
            null,
            "Concurrency retry limit was reached before the purchase could commit.");
    }

    private async Task<int> GetCurrentStockAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Products
            .AsNoTracking()
            .Where(product => product.Id == ProductId)
            .Select(product => product.StockQuantity)
            .SingleAsync(cancellationToken);
    }

    private static class PurchaseStatus
    {
        public const string Success = "Success";
        public const string OutOfStock = "OutOfStock";
        public const string RetryLimitReached = "RetryLimitReached";
    }

    private sealed record PurchaseAttemptResult(
        int UserNumber,
        string Status,
        int Attempts,
        int ConflictCount,
        int? RemainingStockSeen,
        string Message);
}
