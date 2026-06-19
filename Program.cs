using Microsoft.EntityFrameworkCore;
using ParallelECommerce.Data;
using ParallelECommerce.Endpoints;
using ParallelECommerce.Middleware;
using ParallelECommerce.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add OpenAPI/Swagger services
builder.Services.AddOpenApi();

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Services.AddDbContext<ParallelECommerceDbContext>(options =>
    options.UseSqlServer(defaultConnection));

builder.Services.AddDbContextFactory<ParallelECommerceDbContext>(options =>
    options.UseSqlServer(defaultConnection));

// Register services
builder.Services.AddSingleton<PerformanceMetricsService>();
builder.Services.AddSingleton<ResourceMonitoringService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ResourceMonitoringService>());

var useRedisCache = bool.TryParse(builder.Configuration["Cache:UseRedis"], out var parsedUseRedis) && parsedUseRedis;
var redisConnectionString = builder.Configuration["Cache:RedisConnectionString"];

if (useRedisCache && !string.IsNullOrWhiteSpace(redisConnectionString))
{
    // Production-like distributed cache provider. Use Docker/Redis or a hosted Redis instance.
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "ParallelECommerce:";
    });

    // Shared Redis connection used by Requirement 07 for atomic distributed locks.
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    });

    builder.Services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
}
else
{
    // Safe local fallback for demos when Redis is not available.
    // The application code still depends on IDistributedCache, so switching to Redis only needs configuration.
    builder.Services.AddDistributedMemoryCache();

    // Local fallback for development only. For the final distributed-lock proof, enable Redis.
    builder.Services.AddSingleton<IDistributedLockService, InMemoryDistributedLockService>();
}

builder.Services.AddSingleton<InventoryService>();
builder.Services.AddSingleton<CachingCatalogService>();
builder.Services.AddSingleton<DistributedLockDemoService>();
builder.Services.AddScoped<DatabaseConcurrencyService>();
builder.Services.AddScoped<DatabaseTransactionService>();
builder.Services.AddSingleton<CapacityControlService>();
builder.Services.AddSingleton<NotificationQueueService>();
builder.Services.AddHostedService<NotificationWorkerService>();
builder.Services.AddSingleton<BatchProcessingService>();
builder.Services.AddHostedService<BatchJobWorkerService>();
builder.Services.AddSingleton<LoadBalancingService>();
builder.Services.AddSingleton<TransactionIntegrityService>();
builder.Services.AddSingleton<StressTestingService>();
builder.Services.AddSingleton<BottleneckBenchmarkingService>();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "ParallelECommerce API v1");
    });
}

// System health endpoint
app.MapGet("/", () => Results.Ok(new
{
    project = "High-Performance E-Commerce Backend Engine",
    status = "Running",
    message = "Parallel E-Commerce API is ready"
}))
.WithName("HealthCheck")
.WithTags("System");

// AOP-style cross-cutting performance monitoring for all API requests.
app.UseMiddleware<PerformanceMonitoringMiddleware>();

// Register endpoint groups
app.MapMonitoringEndpoints();
app.MapInventoryEndpoints();
app.MapCachingEndpoints();
app.MapDistributedLockEndpoints();
app.MapDatabaseConcurrencyEndpoints();
app.MapCapacityControlEndpoints();
app.MapAsyncProcessingEndpoints();
app.MapBatchProcessingEndpoints();
app.MapLoadBalancingEndpoints();
app.MapTransactionIntegrityEndpoints();
app.MapDatabaseTransactionEndpoints();
app.MapStressTestingEndpoints();
app.MapBottleneckBenchmarkingEndpoints();
app.Run();
