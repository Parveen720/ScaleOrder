
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.DTOs;
using PaymentService.Services;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IWebHostEnvironment _environment;

    public PaymentController(
        IPaymentService paymentService,
        IWebHostEnvironment environment)
    {
        _paymentService = paymentService;
        _environment = environment;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePayment(
        [FromBody] CreatePaymentRequest request)
    {
        var userIdValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var idempotencyKey =
            Request.Headers["Idempotency-Key"].ToString();

        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 128)
        {
            return BadRequest(new
            {
                message = "A valid Idempotency-Key header is required."
            });
        }

        if (request.SimulateFailure && !_environment.IsDevelopment())
        {
            return BadRequest(new
            {
                message = "Payment failure simulation is disabled."
            });
        }

        var authorizationHeader =
            Request.Headers.Authorization.ToString();

        if (!authorizationHeader.StartsWith(
                "Bearer ",
                StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized();
        }

        var accessToken =
            authorizationHeader["Bearer ".Length..].Trim();

        try
        {
            var payment = await _paymentService.CreatePaymentAsync(
                userId,
                request,
                accessToken,
                idempotencyKey);

            return CreatedAtAction(
                nameof(GetPayment),
                new { id = payment.Id },
                payment);
        }
        catch (IdempotencyConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new
            {
                message = "OrderService is unavailable or returned an error."
            });
        }
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Ok(new
        {
            service = "PaymentService",
            status = "Healthy"
        });
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyPayments()
    {
        var userIdValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var payments =
            await _paymentService.GetPaymentsByUserIdAsync(userId);

        return Ok(payments);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetPayment(Guid id)
    {
        var userIdValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var payment =
            await _paymentService.GetPaymentByIdAsync(id);

        if (payment == null || payment.UserId != userId)
            return NotFound();

        return Ok(new PaymentResponseDto
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString(),
            Provider = payment.Provider,
            ProviderReference = payment.ProviderReference,
            CreatedAt = payment.CreatedAt
        });
    }
}
