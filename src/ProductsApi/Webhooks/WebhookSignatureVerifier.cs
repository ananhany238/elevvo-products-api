using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ProductsApi.Webhooks;

public enum SignatureResult
{
    Valid,
    InvalidTimestamp,
    StaleTimestamp,
    InvalidSignature
}

// Signing scheme (documented in the README):
//   signed bytes = ASCII(timestamp) + "." + exact raw request-body bytes
//   signature    = HMAC-SHA256(secret, signed bytes), sent as "sha256=" + lowercase hex
//   headers      = X-Webhook-Timestamp (unix seconds), X-Webhook-Signature
public class WebhookSignatureVerifier
{
    public const string SignaturePrefix = "sha256=";
    private const int Sha256HexLength = 64;

    private readonly WebhookOptions _options;
    private readonly TimeProvider _time;

    public WebhookSignatureVerifier(IOptions<WebhookOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public SignatureResult Verify(string? timestampHeader, string? signatureHeader, byte[] body)
    {
        if (!TryParseTimestamp(timestampHeader, out var timestamp))
        {
            return SignatureResult.InvalidTimestamp;
        }

        if (!TryParseSignature(signatureHeader, out var providedSignature))
        {
            return SignatureResult.InvalidSignature;
        }

        var expectedSignature = ComputeSignatureBytes(_options.Secret, timestampHeader!, body);

        // Both are 32 bytes here (the length was checked above). The comparison always looks at
        // every byte, so response timing reveals nothing about how much of the signature matched.
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            return SignatureResult.InvalidSignature;
        }

        // Only a correctly signed request gets this far, so the staleness check is not an oracle.
        var ageSeconds = Math.Abs(_time.GetUtcNow().ToUnixTimeSeconds() - timestamp);
        return ageSeconds > _options.ToleranceSeconds
            ? SignatureResult.StaleTimestamp
            : SignatureResult.Valid;
    }

    public static byte[] ComputeSignatureBytes(string secret, string timestamp, byte[] body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");

        var signedBytes = new byte[prefix.Length + body.Length];
        Buffer.BlockCopy(prefix, 0, signedBytes, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, signedBytes, prefix.Length, body.Length);

        return HMACSHA256.HashData(key, signedBytes);
    }

    public static string ComputeSignatureHeader(string secret, string timestamp, byte[] body) =>
        SignaturePrefix + Convert.ToHexString(ComputeSignatureBytes(secret, timestamp, body)).ToLowerInvariant();

    private static bool TryParseTimestamp(string? header, out long timestamp)
    {
        timestamp = 0;

        // Digits only: no sign, no spaces, bounded length
        return !string.IsNullOrEmpty(header)
               && header.Length <= 12
               && long.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out timestamp);
    }

    private static bool TryParseSignature(string? header, out byte[] signature)
    {
        signature = Array.Empty<byte>();

        if (string.IsNullOrEmpty(header) || !header.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var hex = header.AsSpan(SignaturePrefix.Length);
        if (hex.Length != Sha256HexLength)
        {
            return false;
        }

        try
        {
            signature = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
