using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProductsApi.Webhooks;

namespace ProductsApi.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebhookOptions _options;
    private readonly WebhookSignatureVerifier _verifier;
    private readonly WebhookEventProcessor _processor;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IOptions<WebhookOptions> options,
        WebhookSignatureVerifier verifier,
        WebhookEventProcessor processor,
        ILogger<WebhooksController> logger)
    {
        _options = options.Value;
        _verifier = verifier;
        _processor = processor;
        _logger = logger;
    }

    // Status codes:
    //   201 processed      200 duplicate (already processed, nothing changed)
    //   400 malformed_json / invalid_schema
    //   401 invalid_timestamp / invalid_signature / stale_timestamp
    //   422 unprocessable_event (product missing, or stock would go below zero)
    //   503 not_configured (no signing secret set)
    [HttpPost("events")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogError("Webhook rejected: no signing secret is configured");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "not_configured" });
        }

        // 1. Keep the EXACT bytes that were sent. The signature is computed over these bytes,
        //    so the body must not be parsed and re-serialized before it is verified.
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var rawBody = buffer.ToArray();

        // 2. Authenticate before trusting anything in the body
        var timestamp = Request.Headers["X-Webhook-Timestamp"].FirstOrDefault();
        var signature = Request.Headers["X-Webhook-Signature"].FirstOrDefault();

        switch (_verifier.Verify(timestamp, signature, rawBody))
        {
            case SignatureResult.InvalidTimestamp:
                _logger.LogWarning("Webhook rejected: missing or malformed timestamp");
                return Unauthorized(new { error = "invalid_timestamp" });

            case SignatureResult.InvalidSignature:
                _logger.LogWarning("Webhook rejected: missing, malformed or wrong signature");
                return Unauthorized(new { error = "invalid_signature" });

            case SignatureResult.StaleTimestamp:
                _logger.LogWarning("Webhook rejected: timestamp outside the allowed tolerance");
                return Unauthorized(new { error = "stale_timestamp" });
        }

        // 3. Parse and validate (only reachable with a valid signature)
        WebhookEventRequest? evt;
        try
        {
            evt = JsonSerializer.Deserialize<WebhookEventRequest>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Webhook rejected: body is not valid JSON");
            return BadRequest(new { error = "malformed_json" });
        }

        var problems = WebhookEventValidator.Validate(evt);
        if (problems.Count > 0)
        {
            _logger.LogWarning("Webhook rejected: schema validation failed ({Count} problems)", problems.Count);
            return BadRequest(new { error = "invalid_schema", problems });
        }

        // 4. Process idempotently (the event id is validated, so it is safe to log)
        var outcome = await _processor.ProcessAsync(evt!, ct);

        return outcome switch
        {
            WebhookOutcome.Processed =>
                StatusCode(StatusCodes.Status201Created, new { status = "processed", eventId = evt!.Id }),
            WebhookOutcome.Duplicate =>
                Ok(new { status = "duplicate", eventId = evt!.Id }),
            WebhookOutcome.ProductNotFound =>
                UnprocessableEntity(new { error = "unprocessable_event", message = "product not found" }),
            _ =>
                UnprocessableEntity(new { error = "unprocessable_event", message = "insufficient stock" })
        };
    }
}
