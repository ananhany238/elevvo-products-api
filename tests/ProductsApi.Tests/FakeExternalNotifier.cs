using ProductsApi.Outbox;

namespace ProductsApi.Tests;

// Records every call and can be told to fail the first N times
public class FakeExternalNotifier : IExternalNotifier
{
    public List<string> CalledWithKeys { get; } = new();
    public int FailuresRemaining { get; set; }
    public bool AlwaysFail { get; set; }

    public Task NotifyAsync(string idempotencyKey, string type, string payload, CancellationToken ct)
    {
        CalledWithKeys.Add(idempotencyKey);

        if (AlwaysFail || FailuresRemaining > 0)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
            }

            throw new InvalidOperationException("external system is down");
        }

        return Task.CompletedTask;
    }
}
