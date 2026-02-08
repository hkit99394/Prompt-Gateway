namespace ControlPlane.Core;

public interface IJobStore
{
    Task CreateAsync(JobRecord job, CancellationToken cancellationToken);
    Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken);
    Task UpdateAsync(JobRecord job, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobSummary>> ListAsync(int limit, CancellationToken cancellationToken);
}

public interface IJobEventStore
{
    Task AppendAsync(JobEvent jobEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobEvent>> GetAsync(string jobId, CancellationToken cancellationToken);
}

public interface IOutboxStore
{
    Task EnqueueDispatchAsync(OutboxDispatchMessage message, CancellationToken cancellationToken);
    Task<OutboxDispatchMessage?> TryDequeueAsync(CancellationToken cancellationToken);
    Task MarkDispatchedAsync(string outboxId, CancellationToken cancellationToken);
    Task ReleaseAsync(string outboxId, CancellationToken cancellationToken);
    Task MarkFailedAsync(string outboxId, string reason, CancellationToken cancellationToken);
}

public interface IDeduplicationStore
{
    Task<bool> TryStartAsync(string jobId, string attemptId, CancellationToken cancellationToken);
    Task MarkCompletedAsync(string jobId, string attemptId, CancellationToken cancellationToken);
}

public interface IResultStore
{
    Task SaveAttemptResultAsync(
        string jobId,
        string attemptId,
        CanonicalResponse response,
        CancellationToken cancellationToken);

    Task SaveFinalResultAsync(string jobId, CanonicalResponse response, CancellationToken cancellationToken);
    Task<CanonicalResponse?> GetFinalResultAsync(string jobId, CancellationToken cancellationToken);
}
