using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain;
using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class JwtTokenService(
    IOptions<JwtOptions> options,
    IOptions<AdminOptions> adminOptions) : IJwtTokenService
{
    private readonly JwtOptions _jwtOptions = options.Value;
    private readonly AdminOptions _adminOptions = adminOptions.Value;

    public AuthResponse CreateToken(User user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_jwtOptions.ExpirationMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        // Phone-only accounts have no email at all, so these claims are conditional
        // rather than empty strings — an empty claim reads as "verified as nothing".
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            claims.Add(new Claim(ClaimTypes.MobilePhone, user.PhoneNumber));
        }

        // Decided here rather than read straight off the row, because this is the one place
        // the role becomes a fact a request can be checked against. A configured super-admin
        // therefore gets in even if the column says otherwise — which is the whole point of
        // the setting: it is the way back into a console nobody can reach any more.
        var isAdmin = user.IsAdmin || _adminOptions.IsSuperAdmin(user.Email);

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, UserRoles.Admin));
        }

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
            PhoneNumber = user.PhoneNumber,
            DisplayName = user.DisplayName,
            PreferredLanguage = user.PreferredLanguage,
            IsAdmin = isAdmin,
            WelcomeStoryRemaining = user.WelcomeStoryRemaining
        };
    }
}
