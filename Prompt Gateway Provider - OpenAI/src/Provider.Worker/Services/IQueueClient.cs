namespace Provider.Worker.Services;

public interface IQueueClient
{
    Task<QueueReceiveResult> ReceiveMessageAsync(
        QueueReceiveRequest request,
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
