namespace ControlPlane.Core;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class GuidIdGenerator : IIdGenerator
{
    public string NewId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    public string NewTraceId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
