using Amazon.SQS;
using Amazon.SQS.Model;
using Provider.Worker.Services;

namespace Provider.Worker.Aws;

public class SqsQueueClient(IAmazonSQS sqs) : IQueueClient
{
    private readonly IAmazonSQS _sqs = sqs;

    public async Task<QueueReceiveResult> ReceiveMessageAsync(
        QueueReceiveRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = request.QueueUrl,
            MaxNumberOfMessages = request.MaxNumberOfMessages,
            WaitTimeSeconds = request.WaitTimeSeconds,
            VisibilityTimeout = request.VisibilityTimeoutSeconds
        }, cancellationToken);

        var result = new QueueReceiveResult();
        if (response.Messages is null)
        {
            return result;
        }

        foreach (var message in response.Messages)
        {
            result.Messages.Add(new QueueMessage
            {
                Body = message.Body,
                ReceiptHandle = message.ReceiptHandle
            });
        }

        return result;
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
