namespace ControlPlane.Core;

public enum JobState
{
    Created,
    Routed,
    Dispatched,
    Started,
    Completed,
    Failed,
    Retrying,
    Cancelled,
    Expired
}

public enum AttemptState
{
    Created,
    Routed,
    Dispatched,
    Started,
    Completed,
    Failed
}

public enum JobEventType
{
    Created,
    Routed,
    Dispatched,
    Started,
    Completed,
    Failed,
    Retried,
    Cancelled,
    Expired
}

public enum ResultIngestionStatus
{
    Duplicate,
    JobNotFound,
    Finalized,
    Retrying
}
