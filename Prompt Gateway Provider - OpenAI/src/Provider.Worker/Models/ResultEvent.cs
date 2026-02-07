using System.Text.Json.Serialization;

namespace Provider.Worker.Models;

public class ResultEvent
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("attempt_id")]
    public string AttemptId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("response")]
    public CanonicalResponse? Response { get; set; }

    [JsonPropertyName("error")]
    public CanonicalError? Error { get; set; }

    [JsonPropertyName("usage")]
    public UsageMetrics? Usage { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    public static ResultEvent Success(CanonicalJobRequest job, CanonicalResponse response)
    {
        return new ResultEvent
        {
            JobId = job.JobId,
            AttemptId = job.AttemptId,
            Status = "succeeded",
            Response = response,
            Usage = response.Usage
        };
    }

    public static ResultEvent Failure(CanonicalJobRequest job, CanonicalError error)
    {
        return new ResultEvent
        {
            JobId = job.JobId,
            AttemptId = job.AttemptId,
            Status = "failed",
            Error = error
        };
    }
}
