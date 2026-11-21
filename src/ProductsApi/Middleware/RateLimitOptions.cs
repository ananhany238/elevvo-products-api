namespace ProductsApi.Middleware;

public class RateLimitOptions
{
    // Requests allowed per client in one window
    public int PermitLimit { get; set; } = 100;

    // Length of the window in seconds (it starts at the client's first request)
    public int WindowSeconds { get; set; } = 60;
}
