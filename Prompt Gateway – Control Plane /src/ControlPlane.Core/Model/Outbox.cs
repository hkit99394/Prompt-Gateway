namespace ControlPlane.Core;

public sealed class OutboxDispatchMessage
{
    public OutboxDispatchMessage(string outboxId, DispatchMessage message, DateTimeOffset createdAt)
    {
        OutboxId = outboxId;
        Message = message;
        CreatedAt = createdAt;
    }

    public string OutboxId { get; }
    public DispatchMessage Message { get; }
    public DateTimeOffset CreatedAt { get; }
}

public sealed class ResultIngestionOutcome
{
    private ResultIngestionOutcome(
        ResultIngestionStatus status,
        DispatchMessage? dispatch,
        CanonicalResponse? response)
    {
        Status = status;
        Dispatch = dispatch;
        Response = response;
    }

    public ResultIngestionStatus Status { get; }
    public DispatchMessage? Dispatch { get; }
    public CanonicalResponse? Response { get; }

    public static ResultIngestionOutcome Duplicate()
        => new(ResultIngestionStatus.Duplicate, null, null);

    public static ResultIngestionOutcome JobNotFound()
        => new(ResultIngestionStatus.JobNotFound, null, null);

    public static ResultIngestionOutcome Finalized(CanonicalResponse response)
        => new(ResultIngestionStatus.Finalized, null, response);

    public static ResultIngestionOutcome Retrying(DispatchMessage dispatch)
        => new(ResultIngestionStatus.Retrying, dispatch, null);
}
