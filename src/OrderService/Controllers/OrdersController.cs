using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OrderService.DTOs;
using OrderService.Services;

namespace OrderService.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(
        IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(
        CreateOrderRequestDto request,
        [FromHeader(Name = "Idempotency-Key")]
        string? idempotencyKey)
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return BadRequest(new
                {
                    message =
                        "Idempotency-Key header is required."
                });
            }

            var order = await _orderService
                .CreateOrderAsync(
                    userId.Value,
                    request,
                    idempotencyKey);

            return CreatedAtAction(
                nameof(GetOrder),
                new { id = order.Id },
                order);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized();
        }

        var order = await _orderService
            .GetOrderAsync(id);

        if (order == null)
        {
            return NotFound(new
            {
                message = "Order not found."
            });
        }

        if (order.UserId != userId.Value)
        {
            return Forbid();
        }

        return Ok(order);
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> GetMyOrders()
    {
        var userId = GetUserId();

        if (userId == null)
        {
            return Unauthorized();
        }

        var orders = await _orderService
            .GetUserOrdersAsync(userId.Value);

        return Ok(orders);
    }

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(
            ClaimTypes.NameIdentifier);

        if (userIdClaim == null)
        {
            return null;
        }

        if (Guid.TryParse(
            userIdClaim.Value,
            out var userId))
        {
            return userId;
        }

        return null;
    }
}
