using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ParallelECommerce.DTOs;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class DistributedLockEndpoints
{
    public static void MapDistributedLockEndpoints(this WebApplication app)
    {
        app.MapPost("/distributed-lock/reset", (DistributedLockDemoService demoService) =>
        {
            return Results.Ok(demoService.ResetState());
        })
        .WithName("ResetDistributedLockDemo")
        .WithTags("Requirement 07 - Distributed Lock");

        app.MapGet("/distributed-lock/metrics", (DistributedLockDemoService demoService) =>
        {
            return Results.Ok(demoService.GetMetrics());
        })
        .WithName("GetDistributedLockMetrics")
        .WithTags("Requirement 07 - Distributed Lock");

        app.MapPost("/distributed-lock/coupon/before", async (
            CouponRedemptionRequest request,
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.RedeemCouponBeforeAsync(
                request.UserId,
                request.CouponCode,
                cancellationToken);

            return Results.Ok(result);
        })
        .WithName("RedeemCouponBeforeDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock");

        app.MapPost("/distributed-lock/coupon/after", async (
            CouponRedemptionRequest request,
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.RedeemCouponAfterAsync(
                request.UserId,
                request.CouponCode,
                cancellationToken);

            return Results.Ok(result);
        })
        .WithName("RedeemCouponAfterDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock");

        app.MapPost("/distributed-lock/payment/before", async (
            PaymentCallbackRequest request,
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.ProcessPaymentBeforeAsync(
                request.PaymentReference,
                request.CallbackId,
                request.Amount,
                cancellationToken);

            return Results.Ok(result);
        })
        .WithName("ProcessPaymentBeforeDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock");

        app.MapPost("/distributed-lock/payment/after", async (
            PaymentCallbackRequest request,
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.ProcessPaymentAfterAsync(
                request.PaymentReference,
                request.CallbackId,
                request.Amount,
                cancellationToken);

            return Results.Ok(result);
        })
        .WithName("ProcessPaymentAfterDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock");

        app.MapPost("/demo/distributed-lock/coupon/before", async (
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.DemoCouponBeforeAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoCouponBeforeDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock Demo");

        app.MapPost("/demo/distributed-lock/coupon/after", async (
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.DemoCouponAfterAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoCouponAfterDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock Demo");

        app.MapPost("/demo/distributed-lock/payment/before", async (
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.DemoPaymentBeforeAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoPaymentBeforeDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock Demo");

        app.MapPost("/demo/distributed-lock/payment/after", async (
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.DemoPaymentAfterAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoPaymentAfterDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock Demo");

        app.MapPost("/demo/distributed-lock/all", async (
            DistributedLockDemoService demoService,
            CancellationToken cancellationToken) =>
        {
            var result = await demoService.DemoAllAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoAllDistributedLock")
        .WithTags("Requirement 07 - Distributed Lock Demo");
    }
}
