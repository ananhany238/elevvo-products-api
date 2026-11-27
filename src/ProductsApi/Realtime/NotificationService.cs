using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace ProductsApi.Realtime;

// Publishes events to a user's private room, and re-sends events that were not acknowledged
// (a bounded number of times, with growing delays).
public class NotificationService
{
    public const string ClientMethod = "orderStatusChanged";

    public static readonly string[] AllowedStatuses =
        { "created", "paid", "packed", "shipped", "delivered", "cancelled" };

    private readonly IHubContext<NotificationsHub> _hub;
    private readonly PendingNotificationTracker _tracker;
    private readonly TimeProvider _time;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IHubContext<NotificationsHub> hub,
        PendingNotificationTracker tracker,
        TimeProvider time,
        IOptions<NotificationOptions> options,
        ILogger<NotificationService> logger)
    {
        _hub = hub;
        _tracker = tracker;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderStatusEvent> PublishAsync(
        string userId, string orderId, string status, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var evt = new OrderStatusEvent("evt_" + Guid.NewGuid().ToString("N"), orderId, status, now);

        _tracker.Add(userId, evt, now + Backoff(1));
        await SendToRoomAsync(userId, evt, ct);

        _logger.LogInformation(
            "Notification {EventId} sent to the room of user {UserId} (status {Status})", evt.EventId, userId, status);
        return evt;
    }

    // Called by the background worker. Returns how many events were re-sent.
    public async Task<int> RetryDueAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var resent = 0;

        foreach (var pending in _tracker.GetDue(now))
        {
            if (pending.Attempts >= _options.MaxDeliveryAttempts)
            {
                _tracker.Remove(pending.UserId, pending.Event.EventId);
                _logger.LogWarning(
                    "Notification {EventId} for user {UserId} was not acknowledged after {Attempts} attempts, giving up",
                    pending.Event.EventId, pending.UserId, pending.Attempts);
                continue;
            }

            var nextAttempt = now + Backoff(pending.Attempts + 1);
            if (!_tracker.RecordAttempt(pending.UserId, pending.Event.EventId, nextAttempt))
            {
                continue; // acknowledged in the meantime
            }

            await SendToRoomAsync(pending.UserId, pending.Event, ct);
            resent++;
            _logger.LogInformation(
                "Notification {EventId} re-sent to user {UserId} (attempt {Attempt})",
                pending.Event.EventId, pending.UserId, pending.Attempts + 1);
        }

        return resent;
    }

    private Task SendToRoomAsync(string userId, OrderStatusEvent evt, CancellationToken ct) =>
        _hub.Clients.Group(NotificationRooms.RoomFor(userId)).SendAsync(ClientMethod, evt, ct);

    private TimeSpan Backoff(int attemptNumber)
    {
        var milliseconds = Math.Min(30_000d, _options.RetryBaseDelayMs * Math.Pow(2, attemptNumber - 1));
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
