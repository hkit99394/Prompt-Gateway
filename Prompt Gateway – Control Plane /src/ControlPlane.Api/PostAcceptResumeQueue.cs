using System.Threading.Channels;

namespace ControlPlane.Api;

public sealed class PostAcceptResumeQueue : IPostAcceptResumeScheduler
{
    private readonly Channel<string> _channel;
    private readonly HostedWorkerOptions _options;

    public PostAcceptResumeQueue(HostedWorkerOptions options)
    {
        _options = options;
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TrySchedule(string jobId)
    {
        if (!_options.EnablePostAcceptResumeWorker || string.IsNullOrWhiteSpace(jobId))
        {
            return false;
        }

        return _channel.Writer.TryWrite(jobId);
    }

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
