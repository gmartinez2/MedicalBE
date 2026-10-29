using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Medical.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "MedicalBE";
    public string Audience { get; set; } = "MedicalFE";

    /// <summary>Signing key (base64 preferred, ≥ 32 bytes). Supplied via user-secrets or environment — never committed.</summary>
    public string? Key { get; set; }

    public int ExpiryHours { get; set; } = 8;
}

/// <summary>The resolved HMAC signing key, shared by token issuing and validation.</summary>
public sealed class JwtSigningKey(byte[] keyBytes, bool isEphemeral)
{
    public SymmetricSecurityKey Key { get; } = new(keyBytes);
    public bool IsEphemeral { get; } = isEphemeral;

    /// <summary>
    /// Resolves <c>Jwt:Key</c>. When missing: Development gets an ephemeral random key (tokens die on restart),
    /// any other environment fails fast.
    /// </summary>
    public static JwtSigningKey Resolve(string? configuredKey, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (!isDevelopment)
                throw new InvalidOperationException("Jwt:Key is not configured. Set it via environment variables or a secret store.");
            return new JwtSigningKey(RandomNumberGenerator.GetBytes(64), isEphemeral: true);
        }

        byte[] bytes;
        try { bytes = Convert.FromBase64String(configuredKey); }
        catch (FormatException) { bytes = System.Text.Encoding.UTF8.GetBytes(configuredKey); }

        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes (256 bits).");
        return new JwtSigningKey(bytes, isEphemeral: false);
    }
}

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(ApplicationUser user, IEnumerable<string> roles);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, JwtSigningKey signingKey, TimeProvider clock) : IJwtTokenService
{
    public (string Token, DateTime ExpiresAt) CreateToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddHours(opts.ExpiryHours);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = opts.Issuer,
            Audience = opts.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(signingKey.Key, SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
