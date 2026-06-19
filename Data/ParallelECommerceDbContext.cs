using Microsoft.EntityFrameworkCore;
using ParallelECommerce.Entities;

namespace ParallelECommerce.Data;

public sealed class ParallelECommerceDbContext(DbContextOptions<ParallelECommerceDbContext> options)
    : DbContext(options)
{
    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProductEntity>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(product => product.Id);

            entity.Property(product => product.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(product => product.Price)
                .HasColumnType("decimal(18,2)");

            entity.Property(product => product.RowVersion)
                .IsRowVersion();

            entity.HasData(
                new
                {
                    Id = 1,
                    Name = "Demo Laptop",
                    Price = 1750.00m,
                    StockQuantity = 5,
                    PopularityScore = 95
                },
                new
                {
                    Id = 2,
                    Name = "Wireless Headphones",
                    Price = 129.99m,
                    StockQuantity = 25,
                    PopularityScore = 82
                });
        });

        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(order => order.Id);

            entity.Property(order => order.CustomerEmail)
                .HasMaxLength(320)
                .IsRequired();

            entity.Property(order => order.PaymentReference)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(order => order.Amount)
                .HasColumnType("decimal(18,2)");

            entity.Property(order => order.Status)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(order => order.CreatedAtUtc)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(order => order.PaymentReference)
                .IsUnique();

            entity.HasOne(order => order.Product)
                .WithMany()
                .HasForeignKey(order => order.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentEntity>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(payment => payment.Id);

            entity.Property(payment => payment.PaymentReference)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(payment => payment.Amount)
                .HasColumnType("decimal(18,2)");

            entity.Property(payment => payment.Status)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(payment => payment.CapturedAtUtc)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(payment => payment.PaymentReference)
                .IsUnique();
        });
    }
}
