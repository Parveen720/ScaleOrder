
namespace OrderService.Events;

public sealed class PaymentSucceededEvent
{
    public Guid EventId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public DateTime OccurredAt { get; set; }
}
