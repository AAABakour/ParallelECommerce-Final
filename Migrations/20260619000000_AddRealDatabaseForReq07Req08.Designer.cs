using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using ParallelECommerce.Data;

#nullable disable

namespace ParallelECommerce.Migrations;

[DbContext(typeof(ParallelECommerceDbContext))]
[Migration("20260619000000_AddRealDatabaseForReq07Req08")]
partial class AddRealDatabaseForReq07Req08
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.7")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);

        SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

        modelBuilder.Entity("ParallelECommerce.Entities.OrderEntity", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));

            entity.Property<decimal>("Amount")
                .HasColumnType("decimal(18,2)");

            entity.Property<DateTime>("CreatedAtUtc")
                .ValueGeneratedOnAdd()
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property<string>("CustomerEmail")
                .IsRequired()
                .HasMaxLength(320)
                .HasColumnType("nvarchar(320)");

            entity.Property<string>("PaymentReference")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("nvarchar(100)");

            entity.Property<int>("ProductId")
                .HasColumnType("int");

            entity.Property<int>("Quantity")
                .HasColumnType("int");

            entity.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("nvarchar(50)");

            entity.HasKey("Id");

            entity.HasIndex("PaymentReference")
                .IsUnique();

            entity.HasIndex("ProductId");

            entity.ToTable("Orders");
        });

        modelBuilder.Entity("ParallelECommerce.Entities.PaymentEntity", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));

            entity.Property<decimal>("Amount")
                .HasColumnType("decimal(18,2)");

            entity.Property<DateTime>("CapturedAtUtc")
                .ValueGeneratedOnAdd()
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property<string>("PaymentReference")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("nvarchar(100)");

            entity.Property<string>("Status")
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnType("nvarchar(50)");

            entity.HasKey("Id");

            entity.HasIndex("PaymentReference")
                .IsUnique();

            entity.ToTable("Payments");
        });

        modelBuilder.Entity("ParallelECommerce.Entities.ProductEntity", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));

            entity.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnType("nvarchar(200)");

            entity.Property<int>("PopularityScore")
                .HasColumnType("int");

            entity.Property<decimal>("Price")
                .HasColumnType("decimal(18,2)");

            entity.Property<byte[]>("RowVersion")
                .IsRequired()
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("rowversion");

            entity.Property<int>("StockQuantity")
                .HasColumnType("int");

            entity.HasKey("Id");

            entity.ToTable("Products");

            entity.HasData(
                new
                {
                    Id = 1,
                    Name = "Demo Laptop",
                    PopularityScore = 95,
                    Price = 1750.00m,
                    StockQuantity = 5
                },
                new
                {
                    Id = 2,
                    Name = "Wireless Headphones",
                    PopularityScore = 82,
                    Price = 129.99m,
                    StockQuantity = 25
                });
        });

        modelBuilder.Entity("ParallelECommerce.Entities.OrderEntity", entity =>
        {
            entity.HasOne("ParallelECommerce.Entities.ProductEntity", "Product")
                .WithMany()
                .HasForeignKey("ProductId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            entity.Navigation("Product");
        });
#pragma warning restore 612, 618
    }
}
