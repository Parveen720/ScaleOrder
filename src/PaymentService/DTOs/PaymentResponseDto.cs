
namespace PaymentService.DTOs;

public class PaymentResponseDto
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "INR";

    public string Status { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string? ProviderReference { get; set; }

    public DateTime CreatedAt { get; set; }
}

