using Provider.Worker.Models;

namespace Provider.Worker.Services;

public interface IPromptBuilder
{
    string BuildPrompt(CanonicalJobRequest job, string template);
}

public class PromptBuilder : IPromptBuilder
{
    public string BuildPrompt(CanonicalJobRequest job, string template)
    {
        var output = template;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (job.Metadata is not null)
        {
            foreach (var pair in job.Metadata)
            {
                values[pair.Key] = pair.Value;
            }
        }

        if (job.PromptVariables is not null)
        {
            foreach (var pair in job.PromptVariables)
            {
                values[pair.Key] = pair.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(job.PromptInput))
        {
            values["prompt_input"] = job.PromptInput;
        }

        foreach (var pair in values)
        {
            var token = $"{{{{{pair.Key}}}}}";
            output = output.Replace(token, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return output;
    }
}
