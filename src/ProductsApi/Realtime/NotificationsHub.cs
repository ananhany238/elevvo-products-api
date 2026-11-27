using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ProductsApi.Realtime;

// SECURITY MODEL
//  - [Authorize]: no valid token, no connection.
//  - The room is derived from the verified token in OnConnectedAsync. There is NO hub method that
//    lets a client choose or join a room, so there is nothing to forge.
//  - Group membership belongs to one connection. A reconnect is a brand new connection, so the room
//    is derived again from the token presented at that moment.
//  - The only method a client can call is Acknowledge, and it only affects that user's own events.
[Authorize]
public class NotificationsHub : Hub
{
    private readonly PendingNotificationTracker _tracker;
    private readonly ILogger<NotificationsHub> _logger;

    public NotificationsHub(PendingNotificationTracker tracker, ILogger<NotificationsHub> logger)
    {
        _tracker = tracker;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = NotificationRooms.UserIdFrom(Context.User);
        if (userId is null)
        {
            _logger.LogWarning("Connection refused: the token has no valid user id");
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, NotificationRooms.RoomFor(userId));

        // Events that were sent but never acknowledged are delivered again to THIS user only.
        // The client ignores the ones it already showed (same event id).
        foreach (var pending in _tracker.GetPending(userId))
        {
            await Clients.Caller.SendAsync(NotificationService.ClientMethod, pending);
        }

        _logger.LogInformation("User {UserId} connected", userId);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "A connection of user {UserId} closed", NotificationRooms.UserIdFrom(Context.User) ?? "unknown");
        return base.OnDisconnectedAsync(exception);
    }

    // The client confirms it received an event, which stops further retries
    public Task Acknowledge(string eventId)
    {
        var userId = NotificationRooms.UserIdFrom(Context.User);

        if (userId is not null && NotificationRooms.IsValidEventId(eventId))
        {
            _tracker.Acknowledge(userId, eventId);
        }

        return Task.CompletedTask;
    }
}
