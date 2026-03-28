using ControlPlane.Api.Authorizer;

namespace ControlPlane.Core.Tests;

public class ApiKeyAuthorizerTests
{
    [SetUp]
    public void SetUp()
    {
        Environment.SetEnvironmentVariable("ApiSecurity__ApiKey", null);
        Environment.SetEnvironmentVariable("ApiSecurity__ApiKeys__0", null);
        Environment.SetEnvironmentVariable("ApiSecurity__ApiKeys__1", null);
        Environment.SetEnvironmentVariable("ApiSecurity__ApiKeyValueFrom", null);
    }

    [Test]
    public async Task FunctionHandler_ReturnsUnauthorized_WhenHeaderMissing()
    {
        Environment.SetEnvironmentVariable("ApiSecurity__ApiKey", "test-api-key");
        var function = new ApiKeyAuthorizerFunction();

        var response = await function.FunctionHandler(new ApiGatewayAuthorizerRequest
        {
            Headers = new Dictionary<string, string?>()
        });

        Assert.That(response.IsAuthorized, Is.False);
        Assert.That(response.Context["reason"], Is.EqualTo("missing_api_key"));
    }

    [Test]
    public async Task FunctionHandler_ReturnsAuthorized_WhenApiKeyMatches()
    {
        Environment.SetEnvironmentVariable("ApiSecurity__ApiKey", "test-api-key");
        var function = new ApiKeyAuthorizerFunction();

        var response = await function.FunctionHandler(new ApiGatewayAuthorizerRequest
        {
            Headers = new Dictionary<string, string?>
            {
                ["X-API-Key"] = "test-api-key"
            }
        });

        Assert.That(response.IsAuthorized, Is.True);
        Assert.That(response.Context["auth_scheme"], Is.EqualTo("api_key"));
    }

    [Test]
    public async Task FunctionHandler_ReturnsAuthorized_ForRotatedKeyArray()
    {
        Environment.SetEnvironmentVariable("ApiSecurity__ApiKey", "[\"key-one\",\"key-two\"]");
        var function = new ApiKeyAuthorizerFunction();

        var response = await function.FunctionHandler(new ApiGatewayAuthorizerRequest
        {
            Headers = new Dictionary<string, string?>
            {
                ["x-api-key"] = "key-two"
            }
        });

        Assert.That(response.IsAuthorized, Is.True);
    }

    [Test]
    public async Task FunctionHandler_ReturnsUnauthorized_WhenApiKeyDoesNotMatch()
    {
        Environment.SetEnvironmentVariable("ApiSecurity__ApiKey", "test-api-key");
        var function = new ApiKeyAuthorizerFunction();

        var response = await function.FunctionHandler(new ApiGatewayAuthorizerRequest
        {
            Headers = new Dictionary<string, string?>
            {
                ["x-api-key"] = "wrong-key"
            }
        });

        Assert.That(response.IsAuthorized, Is.False);
        Assert.That(response.Context["reason"], Is.EqualTo("invalid_api_key"));
    }
}
