using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ParallelECommerce.DTOs;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class DatabaseTransactionEndpoints
{
    private const string SwaggerTag = "Requirement 08 - Real DB ACID Transaction";

    public static void MapDatabaseTransactionEndpoints(this WebApplication app)
    {
        app.MapPost("/db-transaction/reset", async (
            DatabaseTransactionService transactionService,
            CancellationToken cancellationToken) =>
        {
            var result = await transactionService.ResetAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ResetDatabaseTransactionDemo")
        .WithTags(SwaggerTag);

        app.MapPost("/db-transaction/checkout/after", async (
            CheckoutRequest request,
            DatabaseTransactionService transactionService,
            CancellationToken cancellationToken) =>
        {
            var result = await transactionService.CheckoutAfterAsync(
                request.ProductId,
                request.Quantity,
                request.CustomerEmail,
                request.PaymentReference,
                request.Amount,
                request.SimulateFailureAfterPayment,
                cancellationToken);

            return Results.Ok(result);
        })
        .WithName("CheckoutAfterDatabaseTransaction")
        .WithTags(SwaggerTag);
    }
}
