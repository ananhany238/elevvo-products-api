namespace ProductsApi.Auth;

public class JwtOptions
{
    // HMAC signing key. Comes from the environment (Jwt__Key), never from source control.
    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = "products-api";

    public string Audience { get; set; } = "products-api-clients";

    public int ExpiryMinutes { get; set; } = 30;

    // HS256 needs a long key: at least 32 characters
    public bool IsConfigured => Key.Length >= 32;
}
