namespace ControlPlane.Core;

public sealed class OptimisticConcurrencyException : InvalidOperationException
{
    public OptimisticConcurrencyException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
