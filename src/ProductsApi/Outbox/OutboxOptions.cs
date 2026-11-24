namespace ProductsApi.Outbox;

public class OutboxOptions
{
    // Demo switch: make the simulated external system fail this many times per event before it
    // succeeds, to watch retries in the logs (set with the Outbox__SimulatedFailuresPerEvent variable).
    public int SimulatedFailuresPerEvent { get; set; }
}
