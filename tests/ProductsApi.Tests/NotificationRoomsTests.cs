using System.Security.Claims;
using ProductsApi.Realtime;

namespace ProductsApi.Tests;

public class NotificationRoomsTests
{
    private static ClaimsPrincipal Principal(bool authenticated, params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticated ? "test" : null));

    [Fact]
    public void RoomName_IsDerivedFromTheUserId()
    {
        Assert.Equal("user:alice", NotificationRooms.RoomFor("alice"));
    }

    [Fact]
    public void UserId_IsReadFromTheVerifiedClaims()
    {
        var principal = Principal(true, new Claim(ClaimTypes.NameIdentifier, "alice"));

        Assert.Equal("alice", NotificationRooms.UserIdFrom(principal));
    }

    [Fact]
    public void UserId_FallsBackToTheSubClaim()
    {
        var principal = Principal(true, new Claim("sub", "bob"));

        Assert.Equal("bob", NotificationRooms.UserIdFrom(principal));
    }

    [Fact]
    public void UnauthenticatedPrincipal_HasNoUserId()
    {
        var principal = Principal(false, new Claim(ClaimTypes.NameIdentifier, "alice"));

        Assert.Null(NotificationRooms.UserIdFrom(principal));
        Assert.Null(NotificationRooms.UserIdFrom(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("bob;drop")]
    [InlineData("line\nbreak")]
    [InlineData("trailing\n")]
    [InlineData("user:bob")]
    public void SuspiciousUserIds_AreRejected(string userId)
    {
        var principal = Principal(true, new Claim(ClaimTypes.NameIdentifier, userId));

        Assert.Null(NotificationRooms.UserIdFrom(principal));
    }

    [Fact]
    public void TooLongUserId_IsRejected()
    {
        var principal = Principal(true, new Claim(ClaimTypes.NameIdentifier, new string('a', 51)));

        Assert.Null(NotificationRooms.UserIdFrom(principal));
    }

    [Fact]
    public void EventIds_AreValidated()
    {
        Assert.True(NotificationRooms.IsValidEventId("evt_0123456789abcdef"));
        Assert.False(NotificationRooms.IsValidEventId(null));
        Assert.False(NotificationRooms.IsValidEventId("evt 1"));
        Assert.False(NotificationRooms.IsValidEventId("evt_1\n"));
    }
}
