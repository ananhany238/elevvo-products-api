using ProductsApi.Realtime;

namespace ProductsApi.Tests;

public class PendingNotificationTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static OrderStatusEvent Event(string id) => new(id, "ord_1", "shipped", Now);

    [Fact]
    public void Acknowledge_RemovesOnlyTheCallersOwnEvent()
    {
        var tracker = new PendingNotificationTracker();
        tracker.Add("alice", Event("evt_1"), Now);

        Assert.False(tracker.Acknowledge("bob", "evt_1"));   // not bob's event
        Assert.Equal(1, tracker.Count);

        Assert.True(tracker.Acknowledge("alice", "evt_1"));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void GetPending_IsPerUser()
    {
        var tracker = new PendingNotificationTracker();
        tracker.Add("alice", Event("evt_a"), Now);
        tracker.Add("bob", Event("evt_b"), Now);

        Assert.Equal("evt_a", Assert.Single(tracker.GetPending("alice")).EventId);
        Assert.Equal("evt_b", Assert.Single(tracker.GetPending("bob")).EventId);
        Assert.Empty(tracker.GetPending("carol"));
    }

    [Fact]
    public void GetDue_ReturnsOnlyEventsWhoseRetryTimeHasCome()
    {
        var tracker = new PendingNotificationTracker();
        tracker.Add("alice", Event("evt_soon"), Now.AddSeconds(1));
        tracker.Add("alice", Event("evt_later"), Now.AddSeconds(60));

        var due = tracker.GetDue(Now.AddSeconds(2));

        Assert.Equal("evt_soon", Assert.Single(due).Event.EventId);
    }

    [Fact]
    public void RecordAttempt_CountsAndReschedules_UnlessAlreadyAcknowledged()
    {
        var tracker = new PendingNotificationTracker();
        tracker.Add("alice", Event("evt_1"), Now);

        Assert.True(tracker.RecordAttempt("alice", "evt_1", Now.AddSeconds(5)));
        var pending = Assert.Single(tracker.GetDue(Now.AddSeconds(10)));
        Assert.Equal(2, pending.Attempts);

        tracker.Acknowledge("alice", "evt_1");
        Assert.False(tracker.RecordAttempt("alice", "evt_1", Now.AddSeconds(5)));
    }
}
