using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProductsApi.Auth;
using ProductsApi.Realtime;

namespace ProductsApi.Controllers;

public class SimulateStatusRequest
{
    public string? UserId { get; set; }
    public string? OrderId { get; set; }
    public string? Status { get; set; }
}

// Stands in for "the order system": it publishes a status change to ONE user's private room.
// It is not for end users: it needs the server's simulation key, and it is disabled (404) when
// no key is configured.
[ApiController]
[Route("api/dev")]
public class SimulationController : ControllerBase
{
    private readonly NotificationOptions _options;
    private readonly NotificationService _notifications;
    private readonly DemoUserStore _users;

    public SimulationController(
        IOptions<NotificationOptions> options,
        NotificationService notifications,
        DemoUserStore users)
    {
        _options = options.Value;
        _notifications = notifications;
        _users = users;
    }

    // 202 published | 400 bad request | 401 wrong key | 404 disabled
    [HttpPost("simulate-status")]
    public async Task<IActionResult> SimulateStatus(CancellationToken ct)
    {
        if (!_options.SimulationEnabled)
        {
            return NotFound();
        }

        if (!KeyMatches(Request.Headers["X-Api-Key"].FirstOrDefault()))
        {
            return Unauthorized(new { error = "invalid_api_key" });
        }

        SimulateStatusRequest? body;
        try
        {
            body = await Request.ReadFromJsonAsync<SimulateStatusRequest>(ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return BadRequest(new { error = "malformed_json" });
        }

        if (body is null
            || !NotificationRooms.IsValidUserId(body.UserId)
            || !NotificationRooms.IsValidUserId(body.OrderId)
            || body.Status is null
            || !NotificationService.AllowedStatuses.Contains(body.Status))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                message = "userId and orderId must be 1-50 letters, digits, '_' or '-'; status must be one of: "
                          + string.Join(", ", NotificationService.AllowedStatuses)
            });
        }

        if (!_users.Exists(body.UserId!))
        {
            return BadRequest(new { error = "unknown_user" });
        }

        var evt = await _notifications.PublishAsync(body.UserId!.ToLowerInvariant(), body.OrderId!, body.Status, ct);
        return Accepted(new { evt.EventId, userId = body.UserId.ToLowerInvariant(), evt.OrderId, evt.Status, evt.Timestamp });
    }

    private bool KeyMatches(string? provided)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(_options.SimulationKey));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
