using System.Collections.Concurrent;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class PerformanceMetricsService
{
    private const int MaxResourceSamples = 600;

    private readonly ConcurrentDictionary<string, EndpointMetric> _endpointMetrics = new();
    private readonly List<ResourceSample> _resourceSamples = new();
    private readonly object _resourceSamplesLock = new();

    private long _activeHttpRequests;
    private long _totalHttpRequests;
    private long _failedHttpRequests;
    private long _totalHttpDurationTicks;

    public long ActiveHttpRequests => Interlocked.Read(ref _activeHttpRequests);
    public long TotalHttpRequests => Interlocked.Read(ref _totalHttpRequests);
    public long FailedHttpRequests => Interlocked.Read(ref _failedHttpRequests);

    public void RequestStarted()
    {
        Interlocked.Increment(ref _activeHttpRequests);
    }

    public void RequestFinished(string method, string path, int statusCode, double durationMs, bool failed)
    {
        Interlocked.Decrement(ref _activeHttpRequests);
        Interlocked.Increment(ref _totalHttpRequests);

        if (failed || statusCode >= 500)
        {
            Interlocked.Increment(ref _failedHttpRequests);
        }

        Interlocked.Add(ref _totalHttpDurationTicks, TimeSpan.FromMilliseconds(durationMs).Ticks);

        var endpointKey = $"{method.ToUpperInvariant()} {path}";
        var endpointMetric = _endpointMetrics.GetOrAdd(endpointKey, _ => new EndpointMetric(endpointKey));
        endpointMetric.Record(statusCode, durationMs, failed);
    }

    public object GetSummary()
    {
        var totalRequests = TotalHttpRequests;
        var totalDurationTicks = Interlocked.Read(ref _totalHttpDurationTicks);

        return new
        {
            activeHttpRequests = ActiveHttpRequests,
            totalHttpRequests = totalRequests,
            failedHttpRequests = FailedHttpRequests,
            averageHttpLatencyMs = totalRequests == 0
                ? 0
                : Math.Round(TimeSpan.FromTicks(totalDurationTicks).TotalMilliseconds / totalRequests, 2),
            endpoints = GetEndpointSnapshots()
        };
    }

    public double GetAverageHttpLatencyMs()
    {
        var totalRequests = TotalHttpRequests;

        if (totalRequests == 0)
        {
            return 0;
        }

        var totalDurationTicks = Interlocked.Read(ref _totalHttpDurationTicks);
        return Math.Round(TimeSpan.FromTicks(totalDurationTicks).TotalMilliseconds / totalRequests, 2);
    }

    public List<EndpointMetricSnapshot> GetEndpointSnapshots()
    {
        return _endpointMetrics.Values
            .Select(metric => metric.ToSnapshot())
            .OrderByDescending(snapshot => snapshot.TotalRequests)
            .ThenBy(snapshot => snapshot.Endpoint)
            .ToList();
    }

    public void AddResourceSample(ResourceSample sample)
    {
        lock (_resourceSamplesLock)
        {
            _resourceSamples.Add(sample);

            if (_resourceSamples.Count > MaxResourceSamples)
            {
                _resourceSamples.RemoveRange(0, _resourceSamples.Count - MaxResourceSamples);
            }
        }
    }

    public List<ResourceSample> GetResourceSamples()
    {
        lock (_resourceSamplesLock)
        {
            return _resourceSamples.ToList();
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _activeHttpRequests, 0);
        Interlocked.Exchange(ref _totalHttpRequests, 0);
        Interlocked.Exchange(ref _failedHttpRequests, 0);
        Interlocked.Exchange(ref _totalHttpDurationTicks, 0);

        _endpointMetrics.Clear();

        lock (_resourceSamplesLock)
        {
            _resourceSamples.Clear();
        }
    }

    private sealed class EndpointMetric
    {
        private long _totalRequests;
        private long _failedRequests;
        private long _totalDurationTicks;
        private long _maxDurationTicks;
        private int _lastStatusCode;
        private long _lastRequestAtUtcTicks;

        public EndpointMetric(string endpoint)
        {
            Endpoint = endpoint;
        }

        public string Endpoint { get; }

        public void Record(int statusCode, double durationMs, bool failed)
        {
            Interlocked.Increment(ref _totalRequests);

            if (failed || statusCode >= 500)
            {
                Interlocked.Increment(ref _failedRequests);
            }

            var durationTicks = TimeSpan.FromMilliseconds(durationMs).Ticks;
            Interlocked.Add(ref _totalDurationTicks, durationTicks);
            Interlocked.Exchange(ref _lastStatusCode, statusCode);
            Interlocked.Exchange(ref _lastRequestAtUtcTicks, DateTime.UtcNow.Ticks);

            long previousMax;
            do
            {
                previousMax = Interlocked.Read(ref _maxDurationTicks);

                if (durationTicks <= previousMax)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(ref _maxDurationTicks, durationTicks, previousMax) != previousMax);
        }

        public EndpointMetricSnapshot ToSnapshot()
        {
            var totalRequests = Interlocked.Read(ref _totalRequests);
            var totalDurationTicks = Interlocked.Read(ref _totalDurationTicks);
            var lastRequestTicks = Interlocked.Read(ref _lastRequestAtUtcTicks);

            return new EndpointMetricSnapshot
            {
                Endpoint = Endpoint,
                TotalRequests = totalRequests,
                FailedRequests = Interlocked.Read(ref _failedRequests),
                AverageDurationMs = totalRequests == 0
                    ? 0
                    : Math.Round(TimeSpan.FromTicks(totalDurationTicks).TotalMilliseconds / totalRequests, 2),
                MaxDurationMs = Math.Round(TimeSpan.FromTicks(Interlocked.Read(ref _maxDurationTicks)).TotalMilliseconds, 2),
                LastStatusCode = Interlocked.CompareExchange(ref _lastStatusCode, 0, 0),
                LastRequestAtUtc = lastRequestTicks == 0 ? DateTime.MinValue : new DateTime(lastRequestTicks, DateTimeKind.Utc)
            };
        }
    }
}
