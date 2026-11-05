using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using BodaBoss.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace BodaBoss.Api.Application;

public sealed class AuthTokenService(IConfiguration configuration)
{
    public (string Token, DateTime ExpiresAt) CreateAccessToken(FleetUser user)
    {
        var key = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is required.");
        var issuer = configuration["Jwt:Issuer"] ?? "BodaBoss";
        var audience = configuration["Jwt:Audience"] ?? "BodaBoss.Mobile";
        var expiresAt = DateTime.UtcNow.AddMinutes(15);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new(ClaimTypes.Role, user.Role)
        };
        if (user.Email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }
        if (user.PhoneNumber is not null)
        {
            claims.Add(new Claim(ClaimTypes.MobilePhone, user.PhoneNumber));
        }
        if (user.RiderId is Guid riderId)
        {
            claims.Add(new Claim("rider_id", riderId.ToString()));
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var jwt = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt);
    }

    public static string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
