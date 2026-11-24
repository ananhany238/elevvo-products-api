using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProductsApi.Data;
using ProductsApi.Entities;
using ProductsApi.Outbox;

namespace ProductsApi.Tests;

// Unit tests for the outbox worker logic, on a real (in-memory) SQLite database
public class OutboxProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeExternalNotifier _notifier = new();
    private readonly OutboxProcessor _processor;

    public OutboxProcessorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _processor = new OutboxProcessor(_db, _notifier, _time, NullLogger<OutboxProcessor>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task AddMessageAsync(string eventId)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventId = eventId,
            Type = "stock.adjusted.notification",
            Payload = "{}",
            CreatedAt = now,
            NextAttemptAt = now
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task PendingMessage_IsDeliveredExactlyOnce()
    {
        await AddMessageAsync("evt_1");

        var first = await _processor.ProcessBatchAsync();
        var second = await _processor.ProcessBatchAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(new[] { "evt_1" }, _notifier.CalledWithKeys);
        Assert.NotNull((await _db.OutboxMessages.AsNoTracking().SingleAsync()).ProcessedAt);
    }

    [Fact]
    public async Task FailedDelivery_IsRetriedLaterOnTheSameRow_WithoutDuplicates()
    {
        await AddMessageAsync("evt_retry");
        _notifier.FailuresRemaining = 1;

        // Attempt 1 fails: the message stays pending and is scheduled for later
        var firstPass = await _processor.ProcessBatchAsync();
        var afterFailure = await _db.OutboxMessages.AsNoTracking().SingleAsync();

        Assert.Equal(0, firstPass);
        Assert.Equal(1, afterFailure.Attempts);
        Assert.Null(afterFailure.ProcessedAt);
        Assert.NotNull(afterFailure.LastError);

        // Not due yet: nothing is called again
        await _processor.ProcessBatchAsync();
        Assert.Single(_notifier.CalledWithKeys);

        // Once the backoff has passed, the SAME message is delivered
        _time.Advance(TimeSpan.FromSeconds(10));
        var retryPass = await _processor.ProcessBatchAsync();
        var afterRetry = await _db.OutboxMessages.AsNoTracking().SingleAsync();

        Assert.Equal(1, retryPass);
        Assert.NotNull(afterRetry.ProcessedAt);
        Assert.Equal(2, afterRetry.Attempts);
        Assert.Equal(new[] { "evt_retry", "evt_retry" }, _notifier.CalledWithKeys); // same idempotency key both times
        Assert.Equal(1, await _db.OutboxMessages.CountAsync());                     // still one row, no duplicate
    }

    [Fact]
    public async Task GivesUpAfterTheMaximumNumberOfAttempts()
    {
        await AddMessageAsync("evt_dead");
        _notifier.AlwaysFail = true;

        for (var i = 0; i < OutboxProcessor.MaxAttempts + 3; i++)
        {
            await _processor.ProcessBatchAsync();
            _time.Advance(TimeSpan.FromMinutes(10));
        }

        Assert.Equal(OutboxProcessor.MaxAttempts, _notifier.CalledWithKeys.Count);
        Assert.Null((await _db.OutboxMessages.AsNoTracking().SingleAsync()).ProcessedAt);
    }
}
