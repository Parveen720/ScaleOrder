
namespace PaymentService.Models;

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }

    public Guid UserId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "INR";

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public string Provider { get; set; } = "Simulated";

    public string? ProviderReference { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string RequestFingerprint { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Cancelled
}
