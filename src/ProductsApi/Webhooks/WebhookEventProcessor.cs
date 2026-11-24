using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductsApi.Caching;
using ProductsApi.Data;
using ProductsApi.Entities;

namespace ProductsApi.Webhooks;

public enum WebhookOutcome
{
    Processed,
    Duplicate,
    ProductNotFound,
    InsufficientStock
}

// Idempotent, transactional processing of one (already verified and validated) event.
//
// In ONE database transaction:
//   1. insert the event id   -> the UNIQUE index decides which delivery wins
//   2. change the stock      -> one atomic UPDATE
//   3. insert the outbox row -> the follow-up "external" call, recorded atomically
// Either all three are committed, or none of them are.
public class WebhookEventProcessor
{
    public const string OutboxMessageType = "stock.adjusted.notification";

    private readonly AppDbContext _db;
    private readonly ProductCacheInvalidator _cacheInvalidator;
    private readonly TimeProvider _time;
    private readonly ILogger<WebhookEventProcessor> _logger;

    public WebhookEventProcessor(
        AppDbContext db,
        ProductCacheInvalidator cacheInvalidator,
        TimeProvider time,
        ILogger<WebhookEventProcessor> logger)
    {
        _db = db;
        _cacheInvalidator = cacheInvalidator;
        _time = time;
        _logger = logger;
    }

    public async Task<WebhookOutcome> ProcessAsync(WebhookEventRequest evt, CancellationToken ct = default)
    {
        var eventId = evt.Id!;
        var productId = evt.Data!.ProductId;
        var delta = evt.Data.Delta;
        var now = _time.GetUtcNow().UtcDateTime;

        await using (var transaction = await _db.Database.BeginTransactionAsync(ct))
        {
            // 1. Claim the event id. A concurrent delivery of the same id waits here until this
            //    transaction finishes, then fails on the unique index, so it can never run twice.
            _db.WebhookEvents.Add(new WebhookEvent
            {
                EventId = eventId,
                EventType = evt.Type!,
                ReceivedAt = now
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();

                if (await _db.WebhookEvents.AnyAsync(e => e.EventId == eventId, ct))
                {
                    _logger.LogInformation("Webhook event {EventId}: duplicate delivery, nothing changed", eventId);
                    return WebhookOutcome.Duplicate;
                }

                throw;
            }

            // 2. The side effect: one atomic UPDATE, so two different events for the same product
            //    cannot overwrite each other, and stock can never go below zero.
            var updatedRows = await _db.Products
                .Where(p => p.Id == productId && p.Stock + delta >= 0)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.Stock, p => p.Stock + delta)
                    .SetProperty(p => p.UpdatedAt, now), ct);

            if (updatedRows == 0)
            {
                // Roll back so the event id is NOT recorded: nothing was applied
                await transaction.RollbackAsync(ct);
                var productExists = await _db.Products.AnyAsync(p => p.Id == productId, ct);

                var outcome = productExists ? WebhookOutcome.InsufficientStock : WebhookOutcome.ProductNotFound;
                _logger.LogWarning("Webhook event {EventId}: not applied ({Outcome})", eventId, outcome);
                return outcome;
            }

            // 3. The outbox record for the simulated external side effect
            _db.OutboxMessages.Add(new OutboxMessage
            {
                EventId = eventId,
                Type = OutboxMessageType,
                Payload = JsonSerializer.Serialize(new { eventId, productId, delta }),
                CreatedAt = now,
                NextAttemptAt = now
            });
            await _db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
        }

        // After the commit: cached copies of this product are now outdated
        await _cacheInvalidator.InvalidateProductAsync(productId);

        _logger.LogInformation(
            "Webhook event {EventId}: processed (product {ProductId}, delta {Delta})", eventId, productId, delta);
        return WebhookOutcome.Processed;
    }
}
