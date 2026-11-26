using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ProductsApi.Realtime;

namespace ProductsApi.Auth;

// The two local test users. Only SHA-256 hashes of their passwords are kept in memory,
// and the comparison is constant-time.
public class DemoUserStore
{
    private static readonly byte[] DummyHash = SHA256.HashData(Encoding.UTF8.GetBytes("no-such-user"));

    private readonly Dictionary<string, byte[]> _passwordHashes = new();

    public DemoUserStore(IOptions<DemoAuthOptions> options)
    {
        foreach (var (username, password) in options.Value.Users)
        {
            if (NotificationRooms.IsValidUserId(username) && !string.IsNullOrEmpty(password))
            {
                _passwordHashes[username.ToLowerInvariant()] = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            }
        }
    }

    public bool HasUsers => _passwordHashes.Count > 0;

    public bool Exists(string userId) => _passwordHashes.ContainsKey(userId.ToLowerInvariant());

    public bool TryAuthenticate(string? username, string? password, out string userId)
    {
        var key = (username ?? string.Empty).ToLowerInvariant();
        var found = _passwordHashes.TryGetValue(key, out var storedHash);

        // Always hash and compare, even for unknown users, so timing does not reveal which users exist
        var expected = found ? storedHash! : DummyHash;
        var provided = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
        var matches = CryptographicOperations.FixedTimeEquals(expected, provided);

        userId = found && matches ? key : string.Empty;
        return found && matches;
    }
}
