namespace ProductsApi.Entities;

// One row per webhook event that was processed. The UNIQUE index on EventId is what
// makes processing idempotent: the database decides which delivery "claims" an event id.
public class WebhookEvent
{
    public int Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}
