namespace RUOK.Domain.Tests;

internal sealed class ManualTimeProvider(
    long timestampFrequency = TimeSpan.TicksPerSecond,
    long startingTimestamp = 0) : TimeProvider
{
    private long _timestamp = startingTimestamp;

    public override long TimestampFrequency { get; } = timestampFrequency;

    public override long GetTimestamp() => _timestamp;

    public override DateTimeOffset GetUtcNow() =>
        throw new InvalidOperationException("These tests permit monotonic time only.");

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        _timestamp = checked(_timestamp + (long)((decimal)elapsed.Ticks * TimestampFrequency / TimeSpan.TicksPerSecond));
    }
}
