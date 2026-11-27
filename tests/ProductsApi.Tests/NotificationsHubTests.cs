using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProductsApi.Auth;
using ProductsApi.Realtime;

namespace ProductsApi.Tests;

// Identity and room access: who can connect, who receives what, and what a client cannot do
public class NotificationsHubTests : IClassFixture<ProductsApiFactory>, IAsyncLifetime
{
    private readonly ProductsApiFactory _factory;
    private readonly List<TestNotificationClient> _clients = new();

    public NotificationsHubTests(ProductsApiFactory factory)
    {
        _factory = factory;
    }

    private NotificationService Notifications => _factory.Services.GetRequiredService<NotificationService>();
    private PendingNotificationTracker Tracker => _factory.Services.GetRequiredService<PendingNotificationTracker>();

    public Task InitializeAsync()
    {
        Tracker.Clear(); // the fixture is shared, so start every test without leftovers
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
    }

    private async Task<TestNotificationClient> ConnectAsync(string user, string query = "")
    {
        var client = new TestNotificationClient(_factory, () => TestTokens.For(_factory, user), query: query);
        _clients.Add(client);
        await client.StartAsync();
        return client;
    }

    private TestNotificationClient CreateUnstarted(Func<string?> tokenProvider)
    {
        var client = new TestNotificationClient(_factory, tokenProvider);
        _clients.Add(client);
        return client;
    }

    // ---------- each user gets their own events ----------

    [Fact]
    public async Task User_ReceivesTheirOwnEvent_WithIdStatusAndTimestamp()
    {
        var alice = await ConnectAsync("alice");

        var sent = await Notifications.PublishAsync("alice", "ord_1001", "shipped");

        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 1));
        var received = alice.Raw[0];
        Assert.Equal(sent.EventId, received.EventId);
        Assert.StartsWith("evt_", received.EventId);
        Assert.Equal("ord_1001", received.OrderId);
        Assert.Equal("shipped", received.Status);
        Assert.InRange(received.Timestamp, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task EachUser_ReceivesOnlyTheirOwnEvents()
    {
        var alice = await ConnectAsync("alice");
        var bob = await ConnectAsync("bob");

        await Notifications.PublishAsync("alice", "ord_a1", "paid");
        await Notifications.PublishAsync("bob", "ord_b1", "packed");
        await Notifications.PublishAsync("alice", "ord_a2", "shipped");

        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 2 && bob.Raw.Count == 1));
        await Task.Delay(300); // give a wrongly routed event time to show up

        Assert.Equal(new[] { "ord_a1", "ord_a2" }, alice.Raw.Select(e => e.OrderId).OrderBy(x => x));
        Assert.Equal(new[] { "ord_b1" }, bob.Raw.Select(e => e.OrderId));
    }

    // ---------- cannot reach another user's room ----------

    [Theory]
    [InlineData("JoinGroup")]
    [InlineData("JoinRoom")]
    [InlineData("Subscribe")]
    [InlineData("AddToGroupAsync")]
    public async Task ClientCannotJoinAnotherUsersRoom_BecauseNoSuchMethodExists(string method)
    {
        var alice = await ConnectAsync("alice");

        await Assert.ThrowsAnyAsync<Exception>(() => alice.InvokeAsync(method, "user:bob"));

        await Notifications.PublishAsync("bob", "ord_b1", "shipped");
        await Task.Delay(300);
        Assert.Empty(alice.Raw);
    }

    [Fact]
    public async Task UserIdSentByTheClient_InTheUrl_IsIgnored()
    {
        // Alice's token, but the URL claims she is bob
        var alice = await ConnectAsync("alice", query: "?userId=bob&user=bob&room=user:bob");

        await Notifications.PublishAsync("bob", "ord_for_bob", "shipped");
        var own = await Notifications.PublishAsync("alice", "ord_for_alice", "shipped");

        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count >= 1));
        await Task.Delay(300);
        Assert.Equal(own.EventId, Assert.Single(alice.Raw).EventId);
    }

    // ---------- only verified tokens get in ----------

    [Fact]
    public async Task Connection_WithoutAToken_IsRejected()
    {
        var client = CreateUnstarted(() => null);

        await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());
        Assert.NotEqual(HubConnectionState.Connected, client.State);
    }

    [Fact]
    public async Task Connection_WithGarbageInsteadOfAToken_IsRejected()
    {
        var client = CreateUnstarted(() => "not-a-jwt");

        await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());
    }

    [Fact]
    public async Task Connection_WithATokenSignedWithAnotherKey_IsRejected()
    {
        // Someone who does not know the server's key cannot invent a token for "alice"
        var forger = new JwtTokenService(
            Options.Create(new JwtOptions { Key = "a-completely-different-signing-key-0123456789" }),
            TimeProvider.System);
        var forged = forger.CreateToken("alice").Token;
        var client = CreateUnstarted(() => forged);

        await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());
    }

    [Fact]
    public async Task Connection_WithAnExpiredToken_IsRejected()
    {
        var expired = TestTokens.For(_factory, "alice", TimeSpan.FromMinutes(-10));
        var client = CreateUnstarted(() => expired);

        await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());
    }

    // ---------- disconnects and reconnections ----------

    [Fact]
    public async Task AfterDisconnecting_TheClientReceivesNothingMore()
    {
        var alice = await ConnectAsync("alice");
        await alice.StopAsync();

        await Notifications.PublishAsync("alice", "ord_1", "shipped");
        await Task.Delay(300);

        Assert.Equal(HubConnectionState.Disconnected, alice.State);
        Assert.Empty(alice.Raw);
    }

    [Fact]
    public async Task OnReconnect_TheRoomIsDerivedAgainFromTheTokenPresentedThen()
    {
        // The same client object reconnects, but now holds BOB's token
        var currentUser = "alice";
        var client = new TestNotificationClient(_factory, () => TestTokens.For(_factory, currentUser));
        _clients.Add(client);

        await client.StartAsync();
        await Notifications.PublishAsync("alice", "ord_a1", "paid");
        Assert.True(await TestWait.UntilAsync(() => client.Raw.Count == 1));

        await client.StopAsync();
        currentUser = "bob";
        await client.StartAsync();

        await Notifications.PublishAsync("alice", "ord_a2", "shipped"); // must NOT arrive: the old room is gone
        await Notifications.PublishAsync("bob", "ord_b1", "packed");

        Assert.True(await TestWait.UntilAsync(() => client.Raw.Count == 2));
        await Task.Delay(300);
        Assert.Equal(new[] { "ord_a1", "ord_b1" }, client.Raw.Select(e => e.OrderId));
    }

    [Fact]
    public async Task ReconnectedClient_GetsEventsPublishedAfterTheReconnect()
    {
        var alice = await ConnectAsync("alice");
        await alice.StopAsync();
        await alice.StartAsync();

        await Notifications.PublishAsync("alice", "ord_after", "delivered");

        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 1));
    }
}
