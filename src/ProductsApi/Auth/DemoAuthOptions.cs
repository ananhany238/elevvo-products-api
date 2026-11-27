namespace ProductsApi.Auth;

// Two local test users. Passwords come from the environment, for example
//   Demo__Users__alice=...   Demo__Users__bob=...
public class DemoAuthOptions
{
    public Dictionary<string, string> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
