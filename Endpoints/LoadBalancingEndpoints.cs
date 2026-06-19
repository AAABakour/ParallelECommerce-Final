using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class LoadBalancingEndpoints
{
    public static void MapLoadBalancingEndpoints(this WebApplication app)
    {
        app.MapPost("/load-balancer/reset", (LoadBalancingService loadBalancingService) =>
        {
            return Results.Ok(loadBalancingService.Reset());
        })
        .WithName("ResetLoadBalancerMetrics")
        .WithTags("Load Balancing");

        app.MapGet("/load-balancer/metrics", (LoadBalancingService loadBalancingService) =>
        {
            return Results.Ok(loadBalancingService.GetMetricsSnapshot());
        })
        .WithName("GetLoadBalancerMetrics")
        .WithTags("Load Balancing");

        app.MapGet("/load-balancer/servers", (LoadBalancingService loadBalancingService) =>
        {
            return Results.Ok(loadBalancingService.GetServersSnapshot());
        })
        .WithName("GetLoadBalancerServers")
        .WithTags("Load Balancing");

        app.MapPost("/load-balancer/servers/{serverKey}/health", (string serverKey, bool isHealthy, LoadBalancingService loadBalancingService) =>
        {
            return Results.Ok(loadBalancingService.SetServerHealth(serverKey, isHealthy));
        })
        .WithName("SetLoadBalancerServerHealth")
        .WithTags("Load Balancing");

        app.MapPost("/load-balancer/before/request", async (LoadBalancingService loadBalancingService) =>
        {
            var result = await loadBalancingService.RouteSingleRequestWithoutBalancingAsync();
            return Results.Ok(result);
        })
        .WithName("RouteSingleRequestBeforeLoadBalancing")
        .WithTags("Load Balancing");

        app.MapPost("/load-balancer/after/request", async (LoadBalancingService loadBalancingService) =>
        {
            var result = await loadBalancingService.RouteSingleRequestWithRoundRobinAsync();
            return Results.Ok(result);
        })
        .WithName("RouteSingleRequestAfterLoadBalancing")
        .WithTags("Load Balancing");

        app.MapPost("/demo/load-balancing/before", async (LoadBalancingService loadBalancingService) =>
        {
            const int totalRequests = 30;
            var result = await loadBalancingService.RouteAllRequestsToSingleServerAsync(totalRequests);
            return Results.Ok(result);
        })
        .WithName("DemoLoadBalancingBefore")
        .WithTags("Load Balancing Demo");

        app.MapPost("/demo/load-balancing/after", async (LoadBalancingService loadBalancingService) =>
        {
            const int totalRequests = 30;
            var result = await loadBalancingService.RouteRequestsWithRoundRobinAsync(totalRequests);
            return Results.Ok(result);
        })
        .WithName("DemoLoadBalancingAfter")
        .WithTags("Load Balancing Demo");

        app.MapPost("/demo/load-balancing/after-health-check", async (LoadBalancingService loadBalancingService) =>
        {
            const int totalRequests = 30;
            var result = await loadBalancingService.RouteRequestsWithRoundRobinAndOneUnhealthyServerAsync(totalRequests);
            return Results.Ok(result);
        })
        .WithName("DemoLoadBalancingAfterHealthCheck")
        .WithTags("Load Balancing Demo");
    }
}
