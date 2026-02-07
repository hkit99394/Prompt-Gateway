using Provider.Worker.Models;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class PromptBuilderTests
{
    [Test]
    public void BuildPrompt_ReplacesTokensFromMetadataAndVariables()
    {
        var builder = new PromptBuilder();
        var job = new CanonicalJobRequest
        {
            PromptInput = "input text",
            Metadata = new Dictionary<string, string>
            {
                ["name"] = "Alice"
            },
            PromptVariables = new Dictionary<string, string>
            {
                ["topic"] = "billing"
            }
        };

        var template = "Hello {{name}}, topic={{topic}}, input={{prompt_input}}.";

        var result = builder.BuildPrompt(job, template);

        Assert.That(result, Is.EqualTo("Hello Alice, topic=billing, input=input text."));
    }

    [Test]
    public void BuildPrompt_LeavesUnknownTokensIntact()
    {
        var builder = new PromptBuilder();
        var job = new CanonicalJobRequest();
        var template = "Hello {{missing}}";

        var result = builder.BuildPrompt(job, template);

        Assert.That(result, Is.EqualTo("Hello {{missing}}"));
    }
}
