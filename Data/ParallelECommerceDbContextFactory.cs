using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ParallelECommerce.Data;

public sealed class ParallelECommerceDbContextFactory
    : IDesignTimeDbContextFactory<ParallelECommerceDbContext>
{
    public ParallelECommerceDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        var optionsBuilder = new DbContextOptionsBuilder<ParallelECommerceDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ParallelECommerceDbContext(optionsBuilder.Options);
    }
}
