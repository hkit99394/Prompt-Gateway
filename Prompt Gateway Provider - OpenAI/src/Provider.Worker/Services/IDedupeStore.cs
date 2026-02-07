namespace Provider.Worker.Services;

public enum DedupeDecision
{
    Started,
    DuplicateInProgress,
    DuplicateCompleted
}

public interface IDedupeStore
{
    Task<DedupeDecision> TryStartAsync(string jobId, string attemptId, CancellationToken cancellationToken);
    Task MarkCompletedAsync(string jobId, string attemptId, CancellationToken cancellationToken);
}
