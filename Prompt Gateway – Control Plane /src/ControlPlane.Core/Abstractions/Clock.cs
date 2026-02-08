namespace ControlPlane.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
