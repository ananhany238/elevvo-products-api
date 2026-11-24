using System.Text;
using Microsoft.Extensions.Options;
using ProductsApi.Webhooks;

namespace ProductsApi.Tests;

// Unit tests for the signature scheme, with a clock the test controls
public class WebhookSignatureVerifierTests
{
    private const string Secret = "unit-test-secret";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Body =
        Encoding.UTF8.GetBytes("{\"id\":\"evt_1\",\"type\":\"stock.adjusted\",\"data\":{\"productId\":1,\"delta\":5}}");

    private static WebhookSignatureVerifier CreateVerifier() =>
        new(Options.Create(new WebhookOptions { Secret = Secret, ToleranceSeconds = 300 }),
            new TestTimeProvider(Now));

    private static string TimestampAt(TimeSpan offsetFromNow) =>
        (Now.ToUnixTimeSeconds() + (long)offsetFromNow.TotalSeconds).ToString();

    [Fact]
    public void ValidSignature_IsAccepted()
    {
        var timestamp = TimestampAt(TimeSpan.Zero);
        var signature = WebhookSignatureVerifier.ComputeSignatureHeader(Secret, timestamp, Body);

        Assert.Equal(SignatureResult.Valid, CreateVerifier().Verify(timestamp, signature, Body));
    }

    [Fact]
    public void TamperedBody_IsRejected()
    {
        var timestamp = TimestampAt(TimeSpan.Zero);
        var signature = WebhookSignatureVerifier.ComputeSignatureHeader(Secret, timestamp, Body);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Body).Replace("\"delta\":5", "\"delta\":500"));

        Assert.Equal(SignatureResult.InvalidSignature, CreateVerifier().Verify(timestamp, signature, tampered));
    }

    [Fact]
    public void WrongSecret_IsRejected()
    {
        var timestamp = TimestampAt(TimeSpan.Zero);
        var signature = WebhookSignatureVerifier.ComputeSignatureHeader("another-secret", timestamp, Body);

        Assert.Equal(SignatureResult.InvalidSignature, CreateVerifier().Verify(timestamp, signature, Body));
    }

    [Fact]
    public void ChangingTheTimestampHeader_BreaksTheSignature()
    {
        // The timestamp is part of the signed bytes, so an attacker cannot "refresh" a captured request
        var signedAt = TimestampAt(TimeSpan.FromMinutes(-10));
        var signature = WebhookSignatureVerifier.ComputeSignatureHeader(Secret, signedAt, Body);
        var freshTimestamp = TimestampAt(TimeSpan.Zero);

        Assert.Equal(SignatureResult.InvalidSignature, CreateVerifier().Verify(freshTimestamp, signature, Body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("12 34")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    public void MalformedTimestamp_IsRejected(string? timestamp)
    {
        var signature = WebhookSignatureVerifier.ComputeSignatureHeader(Secret, "1", Body);

        Assert.Equal(SignatureResult.InvalidTimestamp, CreateVerifier().Verify(timestamp, signature, Body));
    }

    [Fact]
    public void MalformedSignatures_AreRejected()
    {
        var timestamp = TimestampAt(TimeSpan.Zero);
        var validHex = Convert.ToHexString(
            WebhookSignatureVerifier.ComputeSignatureBytes(Secret, timestamp, Body)).ToLowerInvariant();

        var malformed = new string?[]
        {
            null,
            "",
            "abc",
            "sha256=",
            WebhookSignatureVerifier.SignaturePrefix + validHex[..63],     // too short
            WebhookSignatureVerifier.SignaturePrefix + validHex + "00",    // too long
            WebhookSignatureVerifier.SignaturePrefix + new string('z', 64), // not hex
            "sha1=" + validHex,                                            // wrong algorithm label
            "SHA256=" + validHex,                                          // prefix is case-sensitive
            validHex                                                       // missing prefix
        };

        foreach (var signature in malformed)
        {
            Assert.Equal(SignatureResult.InvalidSignature, CreateVerifier().Verify(timestamp, signature, Body));
        }
    }

    [Fact]
    public void UppercaseHex_IsAcceptedBecauseItIsTheSameBytes()
    {
        var timestamp = TimestampAt(TimeSpan.Zero);
        var hex = Convert.ToHexString(WebhookSignatureVerifier.ComputeSignatureBytes(Secret, timestamp, Body));

        Assert.Equal(
            SignatureResult.Valid,
            CreateVerifier().Verify(timestamp, WebhookSignatureVerifier.SignaturePrefix + hex.ToUpperInvariant(), Body));
    }

    [Theory]
    [InlineData(-300, SignatureResult.Valid)]        // exactly at the tolerance
    [InlineData(300, SignatureResult.Valid)]
    [InlineData(-301, SignatureResult.StaleTimestamp)]
    [InlineData(301, SignatureResult.StaleTimestamp)] // too far in the future
    [InlineData(-3600, SignatureResult.StaleTimestamp)]
    public void Timestamp_MustBeInsideTheTolerance(int offsetSeconds, SignatureResult expected)
    {
        var timestamp = TimestampAt(TimeSpan.FromSeconds(offsetSeconds));
        var signature = WebhookSignatureVerifier.ComputeSignatureHeader(Secret, timestamp, Body);

        Assert.Equal(expected, CreateVerifier().Verify(timestamp, signature, Body));
    }
}
