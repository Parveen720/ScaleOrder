
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

    public DbSet<ProcessedEvent> ProcessedEvents
        => Set<ProcessedEvent>();

    public DbSet<IdempotencyRecord> IdempotencyRecords
        => Set<IdempotencyRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Order configuration
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

        // OrderItem configuration
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

        // IdempotencyRecord configuration
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

            // Prevent duplicate idempotency keys for the same user.
            entity.HasIndex(i => new
            {
                i.UserId,
                i.Key
            })
            .IsUnique();
        });

        // ProcessedEvent configuration  &  Primary key prevents duplicate event IDs.
        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("ProcessedEvents");

            entity.HasKey(e => e.EventId);

            entity.Property(e => e.EventType)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.ProcessedAt)
                .IsRequired();
        });
    }
}
