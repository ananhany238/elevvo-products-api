using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ProductsApi.Auth;
using ProductsApi.Realtime;

namespace ProductsApi.Tests;

public static class TestTokens
{
    public static string For(WebApplicationFactory<Program> factory, string userId, TimeSpan? lifetime = null) =>
        factory.Services.GetRequiredService<JwtTokenService>().CreateToken(userId, lifetime).Token;
}

public static class TestWait
{
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }
}

// A small SignalR client for tests. It behaves like a careful real client:
//  - it records every delivery (Raw) and the events it would show to a user (Displayed),
//    ignoring an event id it has already shown
//  - it can acknowledge events automatically
public sealed class TestNotificationClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly bool _autoAcknowledge;
    private readonly object _lock = new();
    private readonly HashSet<string> _seen = new();
    private readonly List<OrderStatusEvent> _raw = new();
    private readonly List<OrderStatusEvent> _displayed = new();

    public TestNotificationClient(
        WebApplicationFactory<Program> factory,
        Func<string?> tokenProvider,
        bool autoAcknowledge = true,
        string query = "")
    {
        _autoAcknowledge = autoAcknowledge;

        var handler = factory.Server.CreateHandler();
        var url = new Uri(factory.Server.BaseAddress, "hubs/notifications" + query);

        _connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => handler;
                options.AccessTokenProvider = () => Task.FromResult(tokenProvider());
            })
            .Build();

        _connection.On<OrderStatusEvent>(NotificationService.ClientMethod, OnEventAsync);
    }

    public HubConnectionState State => _connection.State;

    public IReadOnlyList<OrderStatusEvent> Raw
    {
        get
        {
            lock (_lock)
            {
                return _raw.ToList();
            }
        }
    }

    public IReadOnlyList<OrderStatusEvent> Displayed
    {
        get
        {
            lock (_lock)
            {
                return _displayed.ToList();
            }
        }
    }

    public Task StartAsync() => _connection.StartAsync();

    public Task StopAsync() => _connection.StopAsync();

    public Task InvokeAsync(string method, object? argument) => _connection.InvokeAsync(method, argument);

    private async Task OnEventAsync(OrderStatusEvent evt)
    {
        lock (_lock)
        {
            _raw.Add(evt);
            if (_seen.Add(evt.EventId))
            {
                _displayed.Add(evt);
            }
        }

        if (_autoAcknowledge)
        {
            await _connection.InvokeAsync("Acknowledge", evt.EventId);
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
