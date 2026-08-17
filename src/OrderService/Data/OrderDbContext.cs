using Microsoft.EntityFrameworkCore;
using OrderService.Models;

namespace OrderService.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(
        DbContextOptions<OrderDbContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<IdempotencyRecord> IdempotencyRecords
        => Set<IdempotencyRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(o => o.Id);

            entity.Property(o => o.TotalAmount)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(o => o.Status)
                .IsRequired();

            entity.Property(o => o.CreatedAt)
                .IsRequired();

            entity.Property(o => o.UpdatedAt)
                .IsRequired();

            entity.HasIndex(o => o.UserId);

            entity.HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(i => i.Id);

            entity.Property(i => i.UnitPrice)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(i => i.TotalPrice)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(i => i.Quantity)
                .IsRequired();

            entity.HasIndex(i => i.ProductId);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(i => i.Id);

            entity.Property(i => i.Key)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(i => i.UserId)
                .IsRequired();

            entity.Property(i => i.RequestHash)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(i => i.OrderId)
                .IsRequired();

            entity.Property(i => i.ResponseStatusCode)
                .IsRequired();

            entity.Property(i => i.ResponseBody)
                .IsRequired();

            entity.Property(i => i.CreatedAt)
                .IsRequired();

            // Critical for concurrent duplicate requests.
            // The same user cannot use the same key twice.
            entity.HasIndex(i => new
            {
                i.UserId,
                i.Key
            })
            .IsUnique();
        });
    }
}
