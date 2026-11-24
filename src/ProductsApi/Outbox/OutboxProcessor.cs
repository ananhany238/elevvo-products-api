using Microsoft.EntityFrameworkCore;
using ProductsApi.Data;

namespace ProductsApi.Outbox;

// Delivers pending outbox messages. A failed delivery is retried LATER on the SAME row
// (with exponential backoff), so a retry never creates a second event or a second outbox row.
//
// Delivery is "at least once": if the app crashes after the external call but before the row is
// marked as processed, the call is repeated. That is why the event id is sent as an idempotency key.
public class OutboxProcessor
{
    public const int MaxAttempts = 5;

    private readonly AppDbContext _db;
    private readonly IExternalNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        AppDbContext db,
        IExternalNotifier notifier,
        TimeProvider time,
        ILogger<OutboxProcessor> logger)
    {
        _db = db;
        _notifier = notifier;
        _time = time;
        _logger = logger;
    }

    // Returns how many messages were delivered successfully in this pass
    public async Task<int> ProcessBatchAsync(int batchSize = 20, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts && m.NextAttemptAt <= now)
            .OrderBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var delivered = 0;

        foreach (var message in due)
        {
            message.Attempts++;

            try
            {
                await _notifier.NotifyAsync(message.EventId, message.Type, message.Payload, ct);
                message.ProcessedAt = _time.GetUtcNow().UtcDateTime;
                message.LastError = null;
                delivered++;
                _logger.LogInformation(
                    "Outbox: event {EventId} delivered on attempt {Attempt}", message.EventId, message.Attempts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var backoffSeconds = Math.Min(300, 5 * Math.Pow(2, message.Attempts - 1));
                message.LastError = Truncate(ex.Message, 500);
                message.NextAttemptAt = now.AddSeconds(backoffSeconds);
                _logger.LogWarning(
                    "Outbox: event {EventId} failed on attempt {Attempt}, next try in {Seconds}s",
                    message.EventId, message.Attempts, backoffSeconds);
            }

            await _db.SaveChangesAsync(ct);
        }

        return delivered;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
