using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _jwtOptions = options.Value;

    public AuthResponse CreateToken(User user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_jwtOptions.ExpirationMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("subscription_type", user.SubscriptionType.ToString())
        };

        var jwtToken = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials
        );

        return new AuthResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(jwtToken),
            ExpiresAt = expires,
            Email = user.Email,
            SubscriptionType = user.SubscriptionType,
            BookCredits = user.BookCredits
        };
    }
}
