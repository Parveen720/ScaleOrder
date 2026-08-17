namespace OrderService.Models;

public class IdempotencyRecord
{
    public Guid Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    // Hash of the original request body.
    // Used to detect the same key being reused
    // for a different request.
    public string RequestHash { get; set; } = string.Empty;

    // The order created by the original request.
    public Guid OrderId { get; set; }

    // We currently return 201 Created for a successful order.
    // Store it so we can replay the original response.
    public int ResponseStatusCode { get; set; }

    // Serialized OrderResponseDto.
    public string ResponseBody { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

