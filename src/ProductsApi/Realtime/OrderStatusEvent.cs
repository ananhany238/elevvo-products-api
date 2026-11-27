namespace ProductsApi.Realtime;

// What the client receives: an event id (used to ignore duplicates), the new status and a timestamp
public record OrderStatusEvent(string EventId, string OrderId, string Status, DateTimeOffset Timestamp);
