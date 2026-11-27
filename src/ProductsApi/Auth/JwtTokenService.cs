using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ProductsApi.Auth;

// Issues short-lived signed JWTs. The user id lives inside the signed token, so the client
// cannot change it without invalidating the signature.
public class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public bool IsConfigured => _options.IsConfigured;

    // Without a configured key, a random key is used: no token can ever validate (fail closed)
    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options) =>
        options.IsConfigured
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key))
            : new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(string userId, TimeSpan? lifetime = null)
    {
        var now = _time.GetUtcNow();
        var expires = now + (lifetime ?? TimeSpan.FromMinutes(_options.ExpiryMinutes));
        var notBefore = expires < now ? expires.AddMinutes(-1) : now; // allows creating expired tokens for tests

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(JwtRegisteredClaimNames.Sub, userId)
            }),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            NotBefore = notBefore.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateSigningKey(_options), SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expires);
    }
}
