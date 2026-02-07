using Amazon.SQS;
using Amazon.SQS.Model;

namespace Provider.Worker.Services;

public interface ISqsClient
{
    Task<ReceiveMessageResponse> ReceiveMessageAsync(
        ReceiveMessageRequest request,
        CancellationToken cancellationToken);

    Task DeleteMessageAsync(
        string queueUrl,
        string receiptHandle,
        CancellationToken cancellationToken);

    Task ChangeMessageVisibilityAsync(
        string queueUrl,
        string receiptHandle,
        int visibilityTimeoutSeconds,
        CancellationToken cancellationToken);
}

public class SqsClient(IAmazonSQS sqs) : ISqsClient
{
    private readonly IAmazonSQS _sqs = sqs;

    public Task<ReceiveMessageResponse> ReceiveMessageAsync(
        ReceiveMessageRequest request,
        CancellationToken cancellationToken)
    {
        return _sqs.ReceiveMessageAsync(request, cancellationToken);
    }

    public Task DeleteMessageAsync(
        string queueUrl,
        string receiptHandle,
        CancellationToken cancellationToken)
    {
        return _sqs.DeleteMessageAsync(queueUrl, receiptHandle, cancellationToken);
    }

    public Task ChangeMessageVisibilityAsync(
        string queueUrl,
        string receiptHandle,
        int visibilityTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        return _sqs.ChangeMessageVisibilityAsync(
            queueUrl,
            receiptHandle,
            visibilityTimeoutSeconds,
            cancellationToken);
    }
}
