
using Microsoft.EntityFrameworkCore;
using PaymentService.Models;

namespace PaymentService.Data;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(
        DbContextOptions<PaymentDbContext> options)
        : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(p => p.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(p => p.Provider)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(p => p.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(p => p.IdempotencyKey)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(p => p.RequestFingerprint)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(p => p.ProviderReference)
                .HasMaxLength(150);

            entity.HasIndex(p => new
            {
                p.UserId,
                p.IdempotencyKey
            }).IsUnique();

            entity.HasIndex(p => p.OrderId);

            entity.HasIndex(p => new
            {
                p.UserId,
                p.CreatedAt
            });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Payload)
                .IsRequired();

            entity.Property(e => e.Error)
                .HasMaxLength(4000);

            entity.Property(e => e.OccurredAt)
                .IsRequired();

            entity.Property(e => e.RetryCount)
                .IsRequired();

            entity.HasIndex(e => new
            {
                e.ProcessedAt,
                e.OccurredAt
            });
        });
    }
}
