
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
    }
}
