using Microsoft.Extensions.Options;

namespace ProductsApi.Realtime;

public class NotificationRetryWorker : BackgroundService
{
    private readonly NotificationService _notifications;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<NotificationRetryWorker> _logger;

    public NotificationRetryWorker(
        NotificationService notifications,
        IOptions<NotificationOptions> options,
        ILogger<NotificationRetryWorker> logger)
    {
        _notifications = notifications;
        _pollInterval = TimeSpan.FromMilliseconds(Math.Max(50, options.Value.RetryPollMs));
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _notifications.RetryDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification retry pass failed");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
