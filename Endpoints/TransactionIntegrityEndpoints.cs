using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ParallelECommerce.DTOs;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class TransactionIntegrityEndpoints
{
    public static void MapTransactionIntegrityEndpoints(this WebApplication app)
    {
        app.MapPost("/transaction/reset", (TransactionIntegrityService transactionService) =>
        {
            return Results.Ok(transactionService.ResetState());
        })
        .WithName("ResetTransactionIntegrityDemo")
        .WithTags("Requirement 08 - Transaction Integrity");

        app.MapGet("/transaction/metrics", (TransactionIntegrityService transactionService) =>
        {
            return Results.Ok(transactionService.GetMetrics());
        })
        .WithName("GetTransactionIntegrityMetrics")
        .WithTags("Requirement 08 - Transaction Integrity");

        app.MapPost("/transaction/checkout/before", async (
            CheckoutRequest request,
            TransactionIntegrityService transactionService,
            CancellationToken cancellationToken) =>
        {
            var result = await transactionService.CheckoutBeforeAsync(
                request.ProductId,
                request.Quantity,
                request.CustomerEmail,
                request.PaymentReference,
                request.Amount,
                request.SimulateFailureAfterPayment,
                cancellationToken);

            return Results.Ok(result);
        })
        .WithName("CheckoutBeforeTransactionIntegrity")
        .WithTags("Requirement 08 - Transaction Integrity");

        app.MapPost("/transaction/checkout/after", async (
            CheckoutRequest request,
            TransactionIntegrityService transactionService,
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
        .WithName("CheckoutAfterTransactionIntegrity")
        .WithTags("Requirement 08 - Transaction Integrity");

        app.MapPost("/demo/transaction/failure/before", async (
            TransactionIntegrityService transactionService,
            CancellationToken cancellationToken) =>
        {
            var result = await transactionService.DemoFailureBeforeAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoTransactionFailureBefore")
        .WithTags("Requirement 08 - Transaction Integrity Demo");

        app.MapPost("/demo/transaction/failure/after", async (
            TransactionIntegrityService transactionService,
            CancellationToken cancellationToken) =>
        {
            var result = await transactionService.DemoFailureAfterAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoTransactionFailureAfter")
        .WithTags("Requirement 08 - Transaction Integrity Demo");

        app.MapPost("/demo/transaction/concurrent/after", async (
            TransactionIntegrityService transactionService,
            CancellationToken cancellationToken) =>
        {
            var result = await transactionService.DemoSuccessfulConcurrentAfterAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoTransactionConcurrentAfter")
        .WithTags("Requirement 08 - Transaction Integrity Demo");

        app.MapPost("/demo/transaction/all", async (
            TransactionIntegrityService transactionService,
            CancellationToken cancellationToken) =>
        {
            var result = await transactionService.DemoAllAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoAllTransactionIntegrity")
        .WithTags("Requirement 08 - Transaction Integrity Demo");
    }
}
