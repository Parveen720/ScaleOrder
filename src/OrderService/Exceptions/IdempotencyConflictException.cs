namespace OrderService.Exceptions;

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException()
        : base(
            "This Idempotency-Key has already been used " +
            "with a different request.")
    {
    }
}