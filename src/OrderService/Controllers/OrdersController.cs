
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderService.DTOs;
using OrderService.Services;
using OrderService.Exceptions;

namespace OrderService.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IConfiguration _configuration;

    public OrdersController(
        IOrderService orderService,
        IConfiguration configuration)
    {
        _orderService = orderService;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        CreateOrderRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        var userId = GetUserId();

        if (userId == null)
            return Unauthorized();

        try
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return BadRequest(new
                {
                    message = "Idempotency-Key header is required."
                });
            }

            var order = await _orderService.CreateOrderAsync(
                userId.Value,
                request,
                idempotencyKey);

            return CreatedAtAction(
                nameof(GetOrder),
                new { id = order.Id },
                order);
        }
        catch (IdempotencyConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var userId = GetUserId();

        if (userId == null)
            return Unauthorized();

        var order = await _orderService.GetOrderAsync(id);

        if (order == null)
        {
            return NotFound(new
            {
                message = "Order not found."
            });
        }

        if (order.UserId != userId.Value)
            return Forbid();

        return Ok(order);
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> GetMyOrders()
    {
        var userId = GetUserId();

        if (userId == null)
            return Unauthorized();

        var orders = await _orderService.GetUserOrdersAsync(
            userId.Value);

        return Ok(orders);
    }

    // Internal endpoint: PaymentService calls this after payment succeeds.
    [HttpPost("internal/{id:guid}/paid")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkOrderAsPaid(
        Guid id,
        [FromHeader(Name = "X-Internal-Service-Secret")]
        string? suppliedSecret)
    {
        var configuredSecret =
            _configuration["InternalServices:ServiceSecret"];

        if (string.IsNullOrWhiteSpace(configuredSecret) ||
            string.IsNullOrWhiteSpace(suppliedSecret))
        {
            return Unauthorized();
        }

        var expectedBytes = Encoding.UTF8.GetBytes(configuredSecret);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedSecret);

        if (expectedBytes.Length != suppliedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(
                expectedBytes,
                suppliedBytes))
        {
            return Unauthorized();
        }

        var updated = await _orderService.MarkOrderAsPaidAsync(id);

        if (!updated)
        {
            return Conflict(new
            {
                message = "Order not found or cannot be marked as paid."
            });
        }

        return Ok(new
        {
            orderId = id,
            status = "Paid"
        });
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (userIdClaim == null)
            return null;

        return Guid.TryParse(userIdClaim.Value, out var userId)
            ? userId
            : null;
    }
}
