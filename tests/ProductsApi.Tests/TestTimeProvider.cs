namespace ProductsApi.Tests;

// A clock the test controls
public class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public TestTimeProvider(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
