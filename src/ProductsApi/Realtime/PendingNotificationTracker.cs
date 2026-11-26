namespace ProductsApi.Realtime;

public record PendingNotification(
    string UserId,
    OrderStatusEvent Event,
    int Attempts,
    DateTimeOffset NextAttemptAt);

// Events that were sent but not acknowledged yet, keyed by (user, event id).
// Keying by the VERIFIED user means a client can only ever acknowledge its own events.
// In memory, so it works for a single instance (use Redis or a database to scale out).
public class PendingNotificationTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<(string UserId, string EventId), PendingNotification> _items = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public void Add(string userId, OrderStatusEvent evt, DateTimeOffset nextAttemptAt)
    {
        lock (_lock)
        {
            _items[(userId, evt.EventId)] = new PendingNotification(userId, evt, 1, nextAttemptAt);
        }
    }

    // Returns false when this user has no such pending event (already acknowledged, or not theirs)
    public bool Acknowledge(string userId, string eventId)
    {
        lock (_lock)
        {
            return _items.Remove((userId, eventId));
        }
    }

    public bool Remove(string userId, string eventId) => Acknowledge(userId, eventId);

    public IReadOnlyList<OrderStatusEvent> GetPending(string userId)
    {
        lock (_lock)
        {
            return _items.Values
                .Where(p => p.UserId == userId)
                .OrderBy(p => p.Event.Timestamp)
                .Select(p => p.Event)
                .ToList();
        }
    }

    public IReadOnlyList<PendingNotification> GetDue(DateTimeOffset now)
    {
        lock (_lock)
        {
            return _items.Values.Where(p => p.NextAttemptAt <= now).ToList();
        }
    }

    // Counts one more send and schedules the next retry. False if it was acknowledged meanwhile.
    public bool RecordAttempt(string userId, string eventId, DateTimeOffset nextAttemptAt)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue((userId, eventId), out var pending))
            {
                return false;
            }

            _items[(userId, eventId)] = pending with
            {
                Attempts = pending.Attempts + 1,
                NextAttemptAt = nextAttemptAt
            };
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }
}
