using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UserService.DTOs;
using UserService.Models;
using UserService.Repositories;

namespace UserService.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;

    public AuthService(
        IUserRepository userRepository,
        IConfiguration configuration)
    {
        _userRepository = userRepository;
        _configuration = configuration;
    }

    public async Task<bool> RegisterAsync(RegisterRequestDto request)
    {
        var email = request.Email
            .Trim()
            .ToLowerInvariant();

        var exists = await _userRepository
            .ExistsByEmailAsync(email);

        if (exists)
        {
            return false;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),

            Name = request.Name.Trim(),

            Email = email,

            PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                request.Password),

            CreatedAt = DateTime.UtcNow,

            UpdatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);

        await _userRepository.SaveChangesAsync();

        return true;
    }

    public async Task<LoginResponseDto?> LoginAsync(
        LoginRequestDto request)
    {
        var email = request.Email
            .Trim()
            .ToLowerInvariant();

        var user = await _userRepository
            .GetByEmailAsync(email);

        if (user == null)
        {
            return null;
        }

        var passwordValid = BCrypt.Net.BCrypt.Verify(
            request.Password,
            user.PasswordHash);

        if (!passwordValid)
        {
            return null;
        }

        var token = GenerateJwtToken(user);

        return new LoginResponseDto
        {
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            Token = token
        };
    }

    private string GenerateJwtToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"];
        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException(
                "JWT key is not configured.");
        }

        var claims = new List<Claim>
        {
            new(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()
            ),

            new(
                JwtRegisteredClaimNames.Email,
                user.Email
            ),

            new(
                ClaimTypes.Name,
                user.Name
            ),

            new(
                ClaimTypes.NameIdentifier,
                user.Id.ToString()
            )
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}