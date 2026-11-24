namespace ProductsApi.Entities;

// Transactional outbox: saved in the SAME transaction as the event and the stock change,
// then delivered to the (simulated) external system by a background worker.
public class OutboxMessage
{
    public int Id { get; set; }

    // One outbox message per event (unique), also used as the idempotency key for the external call
    public string EventId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
