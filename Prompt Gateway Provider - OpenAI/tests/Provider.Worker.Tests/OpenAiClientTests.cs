using System.Net;
using System.Text;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class OpenAiClientTests
{
    [Test]
    public async Task ExecuteAsync_ReturnsParsedResponse()
    {
        var json = """
        {
          "model": "gpt-test",
          "choices": [
            { "message": { "content": "hello" }, "finish_reason": "stop" }
          ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 2, "total_tokens": 3 }
        }
        """;

        var handler = new QueueMessageHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }
        });

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com/")
        };

        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            OpenAi = new OpenAiOptions
            {
                ApiKey = "test",
                Model = "gpt-test"
            }
        });

        var sut = new OpenAiClient(client, options);
        var job = new CanonicalJobRequest
        {
            TaskType = CanonicalTaskTypes.ChatCompletion
        };

        var result = await sut.ExecuteAsync(job, "hi", CancellationToken.None);

        Assert.That(result.Content, Is.EqualTo("hello"));
        Assert.That(result.Model, Is.EqualTo("gpt-test"));
        Assert.That(result.Usage?.TotalTokens, Is.EqualTo(3));
        Assert.That(handler.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteAsync_ThrowsOpenAiExceptionOnErrorResponse()
    {
        var errorJson = """
        {
          "error": { "type": "rate_limit_error", "message": "slow down" }
        }
        """;

        var handler = new QueueMessageHandler(new[]
        {
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
            }
        });

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com/")
        };

        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            OpenAi = new OpenAiOptions
            {
                ApiKey = "test",
                Model = "gpt-test"
            }
        });

        var sut = new OpenAiClient(client, options);
        var job = new CanonicalJobRequest();

        var ex = Assert.ThrowsAsync<OpenAiException>(
            async () => await sut.ExecuteAsync(job, "hi", CancellationToken.None));

        Assert.That(ex!.ErrorType, Is.EqualTo("rate_limit_error"));
        Assert.That(ex.RawPayload, Does.Contain("slow down"));
        Assert.That(handler.CallCount, Is.EqualTo(1));
    }

    private sealed class QueueMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No response configured.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
