namespace ControlPlane.Core;

public sealed class SimpleResponseAssembler : ICanonicalResponseAssembler
{
    public Task<CanonicalResponse> AssembleAsync(ProviderResultEvent result, CancellationToken cancellationToken)
    {
        var response = new CanonicalResponse
        {
            Provider = result.Provider,
            Model = result.Model,
            OutputRef = result.OutputRef,
            Usage = result.Usage,
            Cost = result.Cost,
            Error = result.IsSuccess
                ? null
                : result.Error ?? new CanonicalError("provider_error", "Provider returned an error.")
        };

        return Task.FromResult(response);
    }
}
