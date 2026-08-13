namespace Prdb.Ordeno.Infrastructure.Tests.Scanning;

/// <summary>
/// A clock a test moves by hand. Whether a file has stopped being written is
/// decided by how much time passed between two observations, and a test that
/// waited for a real minute to find that out would be a test nobody runs.
/// </summary>
internal sealed class TestTime(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset now = now;

    public TestTime()
        : this(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
