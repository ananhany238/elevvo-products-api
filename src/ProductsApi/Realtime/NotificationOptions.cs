namespace ProductsApi.Realtime;

public class NotificationOptions
{
    // Key for the local simulation endpoint. Empty = the endpoint is disabled (answers 404).
    public string SimulationKey { get; set; } = string.Empty;

    // How many times an event is sent in total (first send + retries) before giving up
    public int MaxDeliveryAttempts { get; set; } = 5;

    // First retry after this long, then it doubles each time (capped at 30 seconds)
    public int RetryBaseDelayMs { get; set; } = 2000;

    // How often the background worker looks for events that need a retry
    public int RetryPollMs { get; set; } = 500;

    public bool SimulationEnabled => !string.IsNullOrEmpty(SimulationKey);
}
