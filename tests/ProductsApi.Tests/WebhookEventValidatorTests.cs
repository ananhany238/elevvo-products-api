using ProductsApi.Webhooks;

namespace ProductsApi.Tests;

public class WebhookEventValidatorTests
{
    private static WebhookEventRequest Valid() =>
        new("evt_1", "stock.adjusted", new StockAdjustedData(1, 5));

    [Fact]
    public void ValidEvent_HasNoProblems()
    {
        Assert.Empty(WebhookEventValidator.Validate(Valid()));
    }

    [Fact]
    public void NullBody_IsRejected()
    {
        Assert.NotEmpty(WebhookEventValidator.Validate(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("new\nline")]
    [InlineData("trailing-newline\n")]
    [InlineData("semi;colon")]
    public void BadEventIds_AreRejected(string? id)
    {
        var evt = Valid() with { Id = id };

        Assert.NotEmpty(WebhookEventValidator.Validate(evt));
    }

    [Fact]
    public void TooLongEventId_IsRejected()
    {
        var evt = Valid() with { Id = new string('a', 101) };

        Assert.NotEmpty(WebhookEventValidator.Validate(evt));
    }

    [Fact]
    public void UnsupportedType_IsRejected()
    {
        Assert.NotEmpty(WebhookEventValidator.Validate(Valid() with { Type = "order.created" }));
    }

    [Fact]
    public void MissingData_IsRejected()
    {
        Assert.NotEmpty(WebhookEventValidator.Validate(Valid() with { Data = null }));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    [InlineData(1, 0)]
    [InlineData(1, 100001)]
    [InlineData(1, -100001)]
    [InlineData(1, int.MinValue)]
    [InlineData(1, int.MaxValue)]
    public void BadData_IsRejected(int productId, int delta)
    {
        var evt = Valid() with { Data = new StockAdjustedData(productId, delta) };

        Assert.NotEmpty(WebhookEventValidator.Validate(evt));
    }

    [Fact]
    public void NegativeDeltaWithinRange_IsAllowed()
    {
        var evt = Valid() with { Data = new StockAdjustedData(1, -3) };

        Assert.Empty(WebhookEventValidator.Validate(evt));
    }
}
