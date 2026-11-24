using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ProductsApi.Outbox;

// The "external system" that must be told about processed events (a warehouse, an email service...)
public interface IExternalNotifier
{
    // idempotencyKey is the event id: the receiving system can use it to ignore a repeated call
    Task NotifyAsync(string idempotencyKey, string type, string payload, CancellationToken ct);
}

// Simulated external system: it only writes to the log (never the payload, which may be sensitive)
public class LoggingExternalNotifier : IExternalNotifier
{
    private readonly ILogger<LoggingExternalNotifier> _logger;
    private readonly int _failuresPerEvent;
    private readonly ConcurrentDictionary<string, int> _calls = new();

    public LoggingExternalNotifier(ILogger<LoggingExternalNotifier> logger, IOptions<OutboxOptions> options)
    {
        _logger = logger;
        _failuresPerEvent = options.Value.SimulatedFailuresPerEvent;
    }

    public Task NotifyAsync(string idempotencyKey, string type, string payload, CancellationToken ct)
    {
        var call = _calls.AddOrUpdate(idempotencyKey, 1, (_, previous) => previous + 1);

        if (call <= _failuresPerEvent)
        {
            throw new InvalidOperationException("Simulated external system failure");
        }

        _logger.LogInformation(
            "Simulated external call delivered: event {EventId}, type {Type}", idempotencyKey, type);
        return Task.CompletedTask;
    }
}
