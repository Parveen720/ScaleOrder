namespace OrderService.Models;

public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}