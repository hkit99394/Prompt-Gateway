namespace ControlPlane.Api;

public interface IPostAcceptResumeScheduler
{
    bool TrySchedule(string jobId);
}
