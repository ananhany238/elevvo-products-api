using System.Security.Claims;
using System.Text.RegularExpressions;

namespace ProductsApi.Realtime;

// The only place that decides which room a user belongs to, and how identity is read.
public static class NotificationRooms
{
    // \z (not $) so a trailing newline can never end up in a room name or a log line
    private static readonly Regex UserIdPattern = new(@"^[A-Za-z0-9_\-]{1,50}\z", RegexOptions.Compiled);
    private static readonly Regex EventIdPattern = new(@"^[A-Za-z0-9_\-]{1,64}\z", RegexOptions.Compiled);

    public static string RoomFor(string userId) => $"user:{userId}";

    public static bool IsValidUserId(string? value) => value is not null && UserIdPattern.IsMatch(value);

    public static bool IsValidEventId(string? value) => value is not null && EventIdPattern.IsMatch(value);

    // The user id comes ONLY from the claims of the verified token (the signature was already
    // checked by the JWT middleware). Nothing the client sends in the URL or in a message is used.
    public static string? UserIdFrom(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? principal.FindFirst("sub")?.Value;

        return IsValidUserId(userId) ? userId : null;
    }
}
