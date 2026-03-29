using Microsoft.EntityFrameworkCore;

namespace MediatorLite.RestApiBenchmarks.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.Name).HasMaxLength(128);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sku).HasMaxLength(64);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.HasIndex(x => x.Sku).IsUnique();
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
            entity.Property(x => x.CreatedAtUtc).HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

            entity.HasOne(x => x.Customer)
                .WithMany(x => x.Orders)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);

            entity.HasOne(x => x.Order)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(64);
            entity.Property(x => x.Payload).HasMaxLength(2048);
            entity.Property(x => x.CreatedAtUtc).HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.HasIndex(x => x.CreatedAtUtc);
        });
    }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public sealed class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int AvailableStock { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
}
