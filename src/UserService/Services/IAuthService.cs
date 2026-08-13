using UserService.DTOs;

namespace UserService.Services;

public interface IAuthService
{
    Task<bool> RegisterAsync(RegisterRequestDto request);

    Task<LoginResponseDto?> LoginAsync(LoginRequestDto request);
}