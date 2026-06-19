using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class LoadBalancingService
{
    private readonly List<ServerNode> _servers = new()
    {
        new ServerNode { Name = "Server A", Weight = 1, SimulatedProcessingMs = 70 },
        new ServerNode { Name = "Server B", Weight = 1, SimulatedProcessingMs = 70 },
        new ServerNode { Name = "Server C", Weight = 1, SimulatedProcessingMs = 70 }
    };

    private readonly object _lock = new();
    private int _roundRobinIndex = -1;
    private int _requestSequence;
    private string _lastStrategy = "Not started";

    public LoadBalancingMetricsSnapshot GetMetricsSnapshot()
    {
        lock (_lock)
        {
            var servers = _servers.Select(ToSnapshot).ToList();

            return new LoadBalancingMetricsSnapshot
            {
                SelectedStrategy = _lastStrategy,
                TotalRequests = servers.Sum(server => server.HandledRequests),
                FailedRequests = servers.Sum(server => server.FailedRequests),
                ActiveRequests = servers.Sum(server => server.ActiveRequests),
                HealthyServers = servers.Count(server => server.IsHealthy),
                UnhealthyServers = servers.Count(server => !server.IsHealthy),
                Servers = servers,
                Explanation = "The metrics show whether requests are concentrated on one server or distributed across healthy servers."
            };
        }
    }

    public List<LoadBalancingServerSnapshot> GetServersSnapshot()
    {
        lock (_lock)
        {
            return _servers.Select(ToSnapshot).ToList();
        }
    }

    public object Reset()
    {
        lock (_lock)
        {
            foreach (var server in _servers)
            {
                server.IsHealthy = true;
                server.HandledRequests = 0;
                server.FailedRequests = 0;
                server.ActiveRequests = 0;
                server.MaxActiveRequestsObserved = 0;
                server.TotalLatencyMs = 0;
            }

            _roundRobinIndex = -1;
            _requestSequence = 0;
            _lastStrategy = "Reset";
        }

        return new
        {
            message = "Load balancing metrics were reset.",
            metrics = GetMetricsSnapshot()
        };
    }

    public object SetServerHealth(string serverKey, bool isHealthy)
    {
        lock (_lock)
        {
            var server = FindServerByKey(serverKey);

            if (server is null)
            {
                return new
                {
                    success = false,
                    message = $"Server '{serverKey}' was not found. Use server-a, server-b, or server-c.",
                    metrics = GetMetricsSnapshot()
                };
            }

            server.IsHealthy = isHealthy;
            _roundRobinIndex = -1;
            _lastStrategy = "Health check simulation";

            return new
            {
                success = true,
                message = $"{server.Name} health was changed to {(isHealthy ? "healthy" : "unhealthy")}. The load balancer will only route to healthy servers.",
                metrics = GetMetricsSnapshot()
            };
        }
    }

    public async Task<LoadBalancedRequestResult> RouteSingleRequestWithoutBalancingAsync()
    {
        ServerNode server;

        lock (_lock)
        {
            _lastStrategy = "BEFORE - Single Server";
            server = _servers[0];
        }

        return await SimulateServerProcessingAsync(
            server,
            mode: "BEFORE - No Load Balancing",
            strategy: "Single Server");
    }

    public async Task<LoadBalancedRequestResult> RouteSingleRequestWithRoundRobinAsync()
    {
        ServerNode selectedServer;

        lock (_lock)
        {
            _lastStrategy = "AFTER - Round Robin";
            selectedServer = GetNextHealthyServerRoundRobinLocked();
        }

        return await SimulateServerProcessingAsync(
            selectedServer,
            mode: "AFTER - Load Balanced",
            strategy: "Round Robin over healthy servers");
    }

    public async Task<object> RouteAllRequestsToSingleServerAsync(int totalRequests)
    {
        Reset();
        var startedAt = DateTime.UtcNow;

        var tasks = Enumerable.Range(1, totalRequests)
            .Select(_ => RouteSingleRequestWithoutBalancingAsync())
            .ToList();

        var results = await Task.WhenAll(tasks);
        var finishedAt = DateTime.UtcNow;

        return new
        {
            mode = "BEFORE - No Load Balancing",
            strategy = "Single Server",
            totalRequests,
            successfulRequests = results.Count(result => result.Success),
            failedRequests = results.Count(result => !result.Success),
            metrics = GetMetricsSnapshot(),
            durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
            problem = "All requests were sent to one server, which can overload it while other servers stay idle."
        };
    }

    public async Task<object> RouteRequestsWithRoundRobinAsync(int totalRequests)
    {
        Reset();
        var startedAt = DateTime.UtcNow;

        var tasks = Enumerable.Range(1, totalRequests)
            .Select(_ => RouteSingleRequestWithRoundRobinAsync())
            .ToList();

        var results = await Task.WhenAll(tasks);
        var finishedAt = DateTime.UtcNow;

        return new
        {
            mode = "AFTER - Load Balanced",
            strategy = "Round Robin",
            totalRequests,
            successfulRequests = results.Count(result => result.Success),
            failedRequests = results.Count(result => !result.Success),
            metrics = GetMetricsSnapshot(),
            durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
            explanation = "Round Robin distributes requests sequentially across healthy servers."
        };
    }

    public async Task<object> RouteRequestsWithRoundRobinAndOneUnhealthyServerAsync(int totalRequests)
    {
        Reset();
        SetServerHealth("server-b", false);

        var startedAt = DateTime.UtcNow;

        var tasks = Enumerable.Range(1, totalRequests)
            .Select(_ => RouteSingleRequestWithRoundRobinAsync())
            .ToList();

        var results = await Task.WhenAll(tasks);
        var finishedAt = DateTime.UtcNow;

        return new
        {
            mode = "AFTER - Round Robin with Health Check",
            strategy = "Round Robin over healthy servers only",
            totalRequests,
            successfulRequests = results.Count(result => result.Success),
            failedRequests = results.Count(result => !result.Success),
            metrics = GetMetricsSnapshot(),
            durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
            explanation = "Server B was marked unhealthy. The load balancer skipped it and distributed requests only to healthy servers."
        };
    }

    private async Task<LoadBalancedRequestResult> SimulateServerProcessingAsync(
        ServerNode server,
        string mode,
        string strategy)
    {
        int requestId;
        var startedAt = DateTime.UtcNow;

        lock (_lock)
        {
            requestId = ++_requestSequence;

            if (!server.IsHealthy)
            {
                server.FailedRequests++;

                return new LoadBalancedRequestResult
                {
                    Mode = mode,
                    Strategy = strategy,
                    RequestId = requestId,
                    SelectedServer = server.Name,
                    Success = false,
                    StatusCode = 503,
                    LatencyMs = 0,
                    Message = $"{server.Name} is unhealthy. The request failed."
                };
            }

            server.ActiveRequests++;
            server.MaxActiveRequestsObserved = Math.Max(server.MaxActiveRequestsObserved, server.ActiveRequests);
        }

        try
        {
            var jitterMs = requestId % 11;
            await Task.Delay(server.SimulatedProcessingMs + jitterMs);

            var finishedAt = DateTime.UtcNow;
            var latencyMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2);

            lock (_lock)
            {
                server.HandledRequests++;
                server.TotalLatencyMs += latencyMs;
            }

            return new LoadBalancedRequestResult
            {
                Mode = mode,
                Strategy = strategy,
                RequestId = requestId,
                SelectedServer = server.Name,
                Success = true,
                StatusCode = 200,
                LatencyMs = latencyMs,
                Message = $"Request {requestId} was handled by {server.Name}."
            };
        }
        finally
        {
            lock (_lock)
            {
                server.ActiveRequests = Math.Max(0, server.ActiveRequests - 1);
            }
        }
    }

    private ServerNode GetNextHealthyServerRoundRobinLocked()
    {
        var healthyServers = _servers
            .Where(server => server.IsHealthy)
            .ToList();

        if (healthyServers.Count == 0)
        {
            throw new InvalidOperationException("No healthy servers are available.");
        }

        _roundRobinIndex = (_roundRobinIndex + 1) % healthyServers.Count;
        return healthyServers[_roundRobinIndex];
    }

    private ServerNode? FindServerByKey(string serverKey)
    {
        var normalized = NormalizeServerKey(serverKey);

        return _servers.FirstOrDefault(server => NormalizeServerKey(server.Name) == normalized);
    }

    private static string NormalizeServerKey(string value)
    {
        return value.Trim().ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");
    }

    private static LoadBalancingServerSnapshot ToSnapshot(ServerNode server)
    {
        return new LoadBalancingServerSnapshot
        {
            Name = server.Name,
            IsHealthy = server.IsHealthy,
            Weight = server.Weight,
            SimulatedProcessingMs = server.SimulatedProcessingMs,
            HandledRequests = server.HandledRequests,
            FailedRequests = server.FailedRequests,
            ActiveRequests = server.ActiveRequests,
            MaxActiveRequestsObserved = server.MaxActiveRequestsObserved,
            AverageLatencyMs = server.HandledRequests == 0
                ? 0
                : Math.Round(server.TotalLatencyMs / server.HandledRequests, 2)
        };
    }
}
