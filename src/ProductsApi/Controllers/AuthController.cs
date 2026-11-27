using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ProductsApi.Auth;

namespace ProductsApi.Controllers;

public class LoginRequest
{
    [Required, StringLength(50)]
    public string? Username { get; set; }

    [Required, StringLength(200)]
    public string? Password { get; set; }
}

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DemoUserStore _users;
    private readonly JwtTokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(DemoUserStore users, JwtTokenService tokens, ILogger<AuthController> logger)
    {
        _users = users;
        _tokens = tokens;
        _logger = logger;
    }

    // 200 token | 400 missing fields | 401 invalid_credentials | 503 not_configured
    [HttpPost("login")]
    public IActionResult Login(LoginRequest request)
    {
        if (!_tokens.IsConfigured || !_users.HasUsers)
        {
            _logger.LogError("Login unavailable: JWT key or demo users are not configured");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        }

        if (!_users.TryAuthenticate(request.Username, request.Password, out var userId))
        {
            // Same answer for an unknown user and a wrong password. The username is not logged.
            _logger.LogWarning("Login failed");
            return Unauthorized(new { error = "invalid_credentials" });
        }

        var (token, expiresAt) = _tokens.CreateToken(userId);
        _logger.LogInformation("User {UserId} logged in", userId);

        return Ok(new { accessToken = token, tokenType = "Bearer", expiresAtUtc = expiresAt });
    }
}
