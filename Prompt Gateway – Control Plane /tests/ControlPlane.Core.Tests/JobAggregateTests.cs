using ControlPlane.Core;

namespace ControlPlane.Core.Tests;

public class JobAggregateTests
{
    [Test]
    public void JobRecord_SetState_ThrowsOnInvalidTransition()
    {
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var job = JobRecord.Create(new CanonicalJobRequest
        {
            JobId = "job-invalid",
            AttemptId = "attempt-invalid",
            TraceId = "trace-invalid",
            TaskType = "chat_completion"
        }, now);

        Assert.Throws<InvalidOperationException>(() =>
            job.SetState(JobState.Completed, now.AddMinutes(1)));
    }

    [Test]
    public void JobAttempt_SetState_ThrowsOnInvalidTransition()
    {
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var attempt = new JobAttempt("attempt-invalid", now);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.SetState(AttemptState.Completed, now.AddMinutes(1)));
    }
}
