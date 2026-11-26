using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProductsApi.Realtime;

namespace ProductsApi.Tests;

// Bonus: acknowledgements, bounded retries, and duplicate protection using event ids
public class NotificationReliabilityTests : IClassFixture<ProductsApiFactory>, IAsyncLifetime
{
    private readonly ProductsApiFactory _factory;
    private readonly List<TestNotificationClient> _clients = new();

    public NotificationReliabilityTests(ProductsApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _factory.Services.GetRequiredService<PendingNotificationTracker>().Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
    }

    private async Task<TestNotificationClient> ConnectAsync(
        WebApplicationFactory<Program> factory, string user, bool autoAcknowledge)
    {
        var client = new TestNotificationClient(factory, () => TestTokens.For(factory, user), autoAcknowledge);
        _clients.Add(client);
        await client.StartAsync();
        return client;
    }

    [Fact]
    public async Task AcknowledgedEvent_IsNotSentAgain()
    {
        var notifications = _factory.Services.GetRequiredService<NotificationService>();
        var tracker = _factory.Services.GetRequiredService<PendingNotificationTracker>();
        var alice = await ConnectAsync(_factory, "alice", autoAcknowledge: true);

        await notifications.PublishAsync("alice", "ord_1", "shipped");
        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 1 && tracker.Count == 0));

        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(150);
            await notifications.RetryDueAsync();
        }

        Assert.Single(alice.Raw);
    }

    [Fact]
    public async Task UnacknowledgedEvent_IsRetried_ThenGivenUpAfterTheMaximumAttempts()
    {
        using var factory = new ProductsApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Configure<NotificationOptions>(options =>
                {
                    options.MaxDeliveryAttempts = 3;
                    options.RetryBaseDelayMs = 50;
                })));
        var notifications = factory.Services.GetRequiredService<NotificationService>();
        var tracker = factory.Services.GetRequiredService<PendingNotificationTracker>();
        var alice = await ConnectAsync(factory, "alice", autoAcknowledge: false);

        var sent = await notifications.PublishAsync("alice", "ord_1", "shipped");

        // Run the retry pass over and over, like the background worker would
        for (var i = 0; i < 25; i++)
        {
            await Task.Delay(80);
            await notifications.RetryDueAsync();
        }

        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 3));
        Assert.All(alice.Raw, e => Assert.Equal(sent.EventId, e.EventId)); // always the SAME event id
        Assert.Equal(0, tracker.Count);                                    // given up: nothing pending

        await Task.Delay(300);
        await notifications.RetryDueAsync();
        Assert.Equal(3, alice.Raw.Count);                                  // bounded: no more sends
    }

    [Fact]
    public async Task ClientShowsAnEventOnlyOnce_EvenWhenItIsDeliveredTwice()
    {
        var notifications = _factory.Services.GetRequiredService<NotificationService>();
        var alice = await ConnectAsync(_factory, "alice", autoAcknowledge: false);

        await notifications.PublishAsync("alice", "ord_1", "shipped");
        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 1));

        // The connection drops and comes back: the server replays what was never acknowledged
        await alice.StopAsync();
        await alice.StartAsync();

        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 2));
        Assert.Equal(alice.Raw[0].EventId, alice.Raw[1].EventId); // delivered twice
        Assert.Single(alice.Displayed);                            // but shown once
    }

    [Fact]
    public async Task AcknowledgingAnotherUsersEvent_DoesNotClearIt()
    {
        var notifications = _factory.Services.GetRequiredService<NotificationService>();
        var tracker = _factory.Services.GetRequiredService<PendingNotificationTracker>();
        var alice = await ConnectAsync(_factory, "alice", autoAcknowledge: false);
        var bob = await ConnectAsync(_factory, "bob", autoAcknowledge: false);

        var forAlice = await notifications.PublishAsync("alice", "ord_a1", "shipped");
        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 1));

        await bob.InvokeAsync("Acknowledge", forAlice.EventId); // bob learns the id somehow and tries it
        await Task.Delay(300);
        Assert.Single(tracker.GetPending("alice"));             // still pending: it is not bob's to acknowledge

        await alice.InvokeAsync("Acknowledge", forAlice.EventId);
        Assert.True(await TestWait.UntilAsync(() => tracker.Count == 0));
    }

    [Fact]
    public async Task PendingEvents_AreReplayedOnlyToTheirOwner()
    {
        var notifications = _factory.Services.GetRequiredService<NotificationService>();
        await notifications.PublishAsync("alice", "ord_while_offline", "shipped"); // nobody is connected

        var bob = await ConnectAsync(_factory, "bob", autoAcknowledge: true);
        await Task.Delay(300);
        Assert.Empty(bob.Raw);                                                      // bob gets nothing of alice's

        var alice = await ConnectAsync(_factory, "alice", autoAcknowledge: true);
        Assert.True(await TestWait.UntilAsync(() => alice.Raw.Count == 1));
        Assert.Equal("ord_while_offline", alice.Raw[0].OrderId);
    }
}
