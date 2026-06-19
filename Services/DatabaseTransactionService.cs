using System.Data;
using Microsoft.EntityFrameworkCore;
using ParallelECommerce.Data;
using ParallelECommerce.Entities;

namespace ParallelECommerce.Services;

public sealed class DatabaseTransactionService(IDbContextFactory<ParallelECommerceDbContext> dbContextFactory)
{
    private const int DemoProductId = 1;
    private const int InitialStock = 5;
    private const string RequirementName = "Requirement 08 - Real DB ACID Transaction";
    private const string IsolationLevelName = "Serializable";

    public async Task<object> ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        var removedOrders = await dbContext.Orders.ExecuteDeleteAsync(cancellationToken);
        var removedPayments = await dbContext.Payments.ExecuteDeleteAsync(cancellationToken);

        var product = await dbContext.Products
            .SingleOrDefaultAsync(product => product.Id == DemoProductId, cancellationToken);

        if (product is null)
        {
            product = new ProductEntity
            {
                Id = DemoProductId,
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
            requirement = RequirementName,
            message = "Database transaction demo state was reset.",
            productId = DemoProductId,
            stockQuantity = product.StockQuantity,
            removedOrders,
            removedPayments
        };
    }

    public async Task<object> CheckoutAfterAsync(
        int productId,
        int quantity,
        string customerEmail,
        string paymentReference,
        decimal amount,
        bool simulateFailureAfterPayment,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return new
            {
                requirement = RequirementName,
                committed = false,
                rolledBack = false,
                reason = "Quantity must be greater than zero."
            };
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var stockBefore = 0;

        try
        {
            var product = await dbContext.Products
                .SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

            if (product is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new
                {
                    requirement = RequirementName,
                    committed = false,
                    rolledBack = true,
                    productId,
                    reason = "Product was not found.",
                    transactionIsolation = IsolationLevelName
                };
            }

            stockBefore = product.StockQuantity;

            if (product.StockQuantity < quantity)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new
                {
                    requirement = RequirementName,
                    committed = false,
                    rolledBack = true,
                    productId,
                    quantity,
                    paymentReference,
                    stockBefore,
                    reason = "Not enough stock. No payment or order was created.",
                    transactionIsolation = IsolationLevelName,
                    proof = await ReadProofStateAsync(productId, paymentReference, cancellationToken)
                };
            }

            product.StockQuantity -= quantity;

            var payment = new PaymentEntity
            {
                PaymentReference = paymentReference,
                Amount = amount,
                Status = "Captured",
                CapturedAtUtc = DateTime.UtcNow
            };

            dbContext.Payments.Add(payment);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (simulateFailureAfterPayment)
            {
                throw new InvalidOperationException("Simulated failure after payment insert and stock update.");
            }

            var order = new OrderEntity
            {
                ProductId = productId,
                Quantity = quantity,
                CustomerEmail = customerEmail,
                PaymentReference = paymentReference,
                Amount = amount,
                Status = "Created",
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var proof = await ReadProofStateAsync(productId, paymentReference, cancellationToken);

            return new
            {
                requirement = RequirementName,
                committed = true,
                rolledBack = false,
                productId,
                quantity,
                paymentReference,
                stockBefore,
                stockAfter = proof.StockQuantity,
                paymentCreated = proof.PaymentExists,
                orderCreated = proof.OrderExists,
                transactionIsolation = IsolationLevelName,
                invariantHolds =
                    proof.PaymentExists &&
                    proof.OrderExists &&
                    proof.StockQuantity == stockBefore - quantity,
                proof = new
                {
                    message = "Payment, stock update, and order creation all persisted after the database transaction committed.",
                    proof.StockQuantity,
                    proof.PaymentExists,
                    proof.OrderExists,
                    proof.PaymentStatus,
                    proof.OrderStatus
                }
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            var proof = await ReadProofStateAsync(productId, paymentReference, cancellationToken);

            return new
            {
                requirement = RequirementName,
                committed = false,
                rolledBack = true,
                productId,
                quantity,
                paymentReference,
                stockBefore,
                stockAfter = proof.StockQuantity,
                paymentCreated = proof.PaymentExists,
                orderCreated = proof.OrderExists,
                transactionIsolation = IsolationLevelName,
                reason = ex.Message,
                invariantHolds =
                    !proof.PaymentExists &&
                    !proof.OrderExists &&
                    proof.StockQuantity == stockBefore,
                proof = new
                {
                    message = "The database transaction rolled back, so the payment insert, stock update, and order insert did not persist.",
                    proof.StockQuantity,
                    proof.PaymentExists,
                    proof.OrderExists,
                    proof.PaymentStatus,
                    proof.OrderStatus
                }
            };
        }
    }

    private async Task<TransactionProofState> ReadProofStateAsync(
        int productId,
        string paymentReference,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stockQuantity = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.StockQuantity)
            .SingleOrDefaultAsync(cancellationToken);

        var payment = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.PaymentReference == paymentReference)
            .Select(payment => new { payment.Status })
            .SingleOrDefaultAsync(cancellationToken);

        var order = await dbContext.Orders
            .AsNoTracking()
            .Where(order => order.PaymentReference == paymentReference)
            .Select(order => new { order.Status })
            .SingleOrDefaultAsync(cancellationToken);

        return new TransactionProofState(
            stockQuantity,
            payment is not null,
            order is not null,
            payment?.Status,
            order?.Status);
    }

    private sealed record TransactionProofState(
        int StockQuantity,
        bool PaymentExists,
        bool OrderExists,
        string? PaymentStatus,
        string? OrderStatus);
}
