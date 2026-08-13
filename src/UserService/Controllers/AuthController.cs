using Microsoft.AspNetCore.Mvc;
using UserService.DTOs;
using UserService.Services;

namespace UserService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequestDto request)
    {
        var result = await _authService
            .RegisterAsync(request);

        if (!result)
        {
            return Conflict(new
            {
                message = "Email is already registered."
            });
        }

        return StatusCode(201, new
        {
            message = "User registered successfully."
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequestDto request)
    {
        var result = await _authService
            .LoginAsync(request);

        if (result == null)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        return Ok(result);
    }
}