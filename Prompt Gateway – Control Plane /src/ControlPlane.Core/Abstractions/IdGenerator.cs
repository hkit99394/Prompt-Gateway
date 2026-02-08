namespace ControlPlane.Core;

public interface IIdGenerator
{
    string NewId(string prefix);
    string NewTraceId();
}
