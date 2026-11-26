// A small live client for the demo:  dotnet run --project tools/NotificationClient -- alice
// It logs in, connects to the hub with its token, shows each event once (ignoring repeated
// event ids), acknowledges what it receives, and logs in again whenever it has to reconnect.
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

var user = args.Length > 0 ? args[0] : null;
var baseUrl = args.Length > 1 ? args[1] : "http://localhost:59059";

if (string.IsNullOrWhiteSpace(user))
{
    Console.WriteLine("Usage: NotificationClient <username> [baseUrl]");
    return 1;
}

var password = Environment.GetEnvironmentVariable($"Demo__Users__{user}");
if (string.IsNullOrEmpty(password))
{
    Console.WriteLine($"No password for '{user}'. Run:  . .\\scripts\\dev-env.ps1");
    return 1;
}

using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

// Called for the first connection AND for every reconnect, so the client always presents a fresh token
async Task<string?> LoginAsync()
{
    var response = await http.PostAsJsonAsync("/api/auth/login", new { username = user, password });
    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"Login failed ({(int)response.StatusCode}).");
        return null;
    }

    var result = await response.Content.ReadFromJsonAsync<LoginResult>();
    return result?.AccessToken;
}

var connection = new HubConnectionBuilder()
    .WithUrl(new Uri(http.BaseAddress!, "/hubs/notifications"), options => options.AccessTokenProvider = LoginAsync)
    .WithAutomaticReconnect()
    .Build();

var seen = new HashSet<string>();

connection.On<NotificationEvent>("orderStatusChanged", async evt =>
{
    if (seen.Add(evt.EventId))
    {
        Console.WriteLine($"[{evt.Timestamp.ToLocalTime():HH:mm:ss}] order {evt.OrderId} is now {evt.Status}   (event {evt.EventId})");
    }
    else
    {
        Console.WriteLine($"(duplicate ignored: event {evt.EventId})");
    }

    await connection.InvokeAsync("Acknowledge", evt.EventId);
});

connection.Reconnecting += _ =>
{
    Console.WriteLine("Connection lost. Reconnecting...");
    return Task.CompletedTask;
};
connection.Reconnected += _ =>
{
    Console.WriteLine("Reconnected: a new connection, a new login, a new room derived from the new token.");
    return Task.CompletedTask;
};
connection.Closed += _ =>
{
    Console.WriteLine("Connection closed.");
    return Task.CompletedTask;
};

await connection.StartAsync();
Console.WriteLine($"Connected as {user}. Waiting for events (Ctrl+C to stop)...");
await Task.Delay(Timeout.Infinite);
return 0;

record LoginResult(string AccessToken);

record NotificationEvent(string EventId, string OrderId, string Status, DateTimeOffset Timestamp);
