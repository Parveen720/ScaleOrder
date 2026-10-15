
namespace PaymentService.Services;

public class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string message)
        : base(message)
    {
    }
}
