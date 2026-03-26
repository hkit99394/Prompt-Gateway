using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Configuration;

namespace Provider.Worker.Lambda;

internal static class LambdaSecretConfiguration
{
    private const string ApiKeyConfigKey = "ProviderWorker:OpenAi:ApiKey";
    private const string ApiKeyValueFromConfigKey = "ProviderWorker:OpenAi:ApiKeyValueFrom";

    public static async Task ApplyAsync(ConfigurationManager configuration, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuration[ApiKeyConfigKey]))
        {
            return;
        }

        var valueFrom = configuration[ApiKeyValueFromConfigKey];
        if (string.IsNullOrWhiteSpace(valueFrom))
        {
            return;
        }

        var apiKey = await ResolveAsync(valueFrom, cancellationToken);
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ApiKeyConfigKey] = apiKey
        });
    }

    private static async Task<string> ResolveAsync(string valueFrom, CancellationToken cancellationToken)
    {
        if (IsSsmReference(valueFrom))
        {
            using var ssm = new AmazonSimpleSystemsManagementClient();
            var response = await ssm.GetParameterAsync(new GetParameterRequest
            {
                Name = valueFrom,
                WithDecryption = true
            }, cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Parameter?.Value))
            {
                throw new InvalidOperationException("Resolved OpenAI API key parameter was empty.");
            }

            return response.Parameter.Value;
        }

        using var secretsManager = new AmazonSecretsManagerClient();
        var secretResponse = await secretsManager.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = valueFrom
        }, cancellationToken);

        if (!string.IsNullOrWhiteSpace(secretResponse.SecretString))
        {
            return secretResponse.SecretString;
        }

        if (secretResponse.SecretBinary is { Length: > 0 })
        {
            return System.Text.Encoding.UTF8.GetString(secretResponse.SecretBinary.ToArray());
        }

        throw new InvalidOperationException("Resolved OpenAI API key secret was empty.");
    }

    private static bool IsSsmReference(string valueFrom)
    {
        return valueFrom.StartsWith("arn:aws:ssm:", StringComparison.OrdinalIgnoreCase)
            || valueFrom.StartsWith("/", StringComparison.Ordinal)
            || valueFrom.StartsWith("parameter/", StringComparison.OrdinalIgnoreCase);
    }
}
