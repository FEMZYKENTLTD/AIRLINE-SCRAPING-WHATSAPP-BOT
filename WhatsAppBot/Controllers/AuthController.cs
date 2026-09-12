using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace WhatsAppBot.Controllers
{
    /// <summary>
    /// Provides JWT authentication for admin API access.
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IConfiguration config, ILogger<AuthController> logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <summary>Authenticate and receive a JWT token.</summary>
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var adminUsername = _config["Admin:Username"]
                ?? Environment.GetEnvironmentVariable("ADMIN_USERNAME")
                ?? "admin";
            var adminPassword = _config["Admin:Password"]
                ?? Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                _logger.LogWarning("Admin password not configured. Authentication disabled.");
                return StatusCode(503, new { error = "Authentication not configured" });
            }

            if (request.Username != adminUsername || request.Password != adminPassword)
            {
                _logger.LogWarning("Failed login attempt for user: {Username}", request.Username);
                return Unauthorized(new { error = "Invalid credentials" });
            }

            var token = GenerateJwtToken(request.Username);

            _logger.LogInformation("Admin login successful: {Username}", request.Username);

            return Ok(new
            {
                token,
                expiresIn = _config.GetValue<int>("Jwt:ExpiryHours", 24) * 3600,
                tokenType = "Bearer"
            });
        }

        private string GenerateJwtToken(string username)
        {
            var secret = _config["Jwt:Secret"]
                ?? Environment.GetEnvironmentVariable("JWT_SECRET")
                ?? throw new InvalidOperationException("JWT secret not configured");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var issuer = _config["Jwt:Issuer"]
                ?? Environment.GetEnvironmentVariable("JWT_ISSUER")
                ?? "AirlineServiceManagement";
            var audience = _config["Jwt:Audience"]
                ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE")
                ?? "AirlineServiceManagement";
            var expiryHours = _config.GetValue<int>("Jwt:ExpiryHours", 24);

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(expiryHours),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
