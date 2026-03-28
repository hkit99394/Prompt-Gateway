using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using ControlPlane.Api.Auth;
using Microsoft.Extensions.Configuration;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ControlPlane.Api.Authorizer;

public sealed class ApiKeyAuthorizerFunction
{
    private const string ApiKeyConfigKey = "ApiSecurity:ApiKey";
    private const string ApiKeyValueFromConfigKey = "ApiSecurity:ApiKeyValueFrom";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static CachedApiKeys? _cachedApiKeys;

    public async Task<ApiGatewayAuthorizerResponse> FunctionHandler(
        ApiGatewayAuthorizerRequest request,
        ILambdaContext? context = null)
    {
        var providedApiKey = TryGetHeader(request.Headers, "x-api-key");
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return ApiGatewayAuthorizerResponse.Unauthorized("missing_api_key");
        }

        var configuredApiKeys = await GetConfiguredApiKeysAsync(CancellationToken.None);
        if (configuredApiKeys.Count == 0)
        {
            return ApiGatewayAuthorizerResponse.Unauthorized("api_keys_not_configured");
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);
        var isAuthorized = configuredApiKeys.Any(configuredApiKey =>
        {
            var configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);
            return CryptographicOperations.FixedTimeEquals(configuredBytes, providedBytes);
        });

        return isAuthorized
            ? ApiGatewayAuthorizerResponse.Authorized("api_key")
            : ApiGatewayAuthorizerResponse.Unauthorized("invalid_api_key");
    }

    private static async Task<IReadOnlyList<string>> GetConfiguredApiKeysAsync(CancellationToken cancellationToken)
    {
        var baseConfiguration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var directKeys = ApiKeyConfiguration.GetConfiguredApiKeys(baseConfiguration);
        if (directKeys.Count > 0)
        {
            return directKeys;
        }

        var valueFrom = baseConfiguration[ApiKeyValueFromConfigKey];
        if (string.IsNullOrWhiteSpace(valueFrom))
        {
            return Array.Empty<string>();
        }

        var now = DateTimeOffset.UtcNow;
        if (_cachedApiKeys is not null
            && string.Equals(_cachedApiKeys.ValueFrom, valueFrom, StringComparison.Ordinal)
            && _cachedApiKeys.ExpiresAt > now)
        {
            return _cachedApiKeys.Keys;
        }

        await CacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedApiKeys is not null
                && string.Equals(_cachedApiKeys.ValueFrom, valueFrom, StringComparison.Ordinal)
                && _cachedApiKeys.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return _cachedApiKeys.Keys;
            }

            var resolvedApiKeys = await ResolveAsync(valueFrom, cancellationToken);
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ApiKeyConfigKey] = resolvedApiKeys
                })
                .Build();

            var keys = ApiKeyConfiguration.GetConfiguredApiKeys(configuration);
            _cachedApiKeys = new CachedApiKeys(valueFrom, DateTimeOffset.UtcNow.Add(CacheDuration), keys);
            return keys;
        }
        finally
        {
            CacheLock.Release();
        }
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
                throw new InvalidOperationException("Resolved authorizer API key parameter was empty.");
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
            return Encoding.UTF8.GetString(secretResponse.SecretBinary.ToArray());
        }

        throw new InvalidOperationException("Resolved authorizer API key secret was empty.");
    }

    private static bool IsSsmReference(string valueFrom)
    {
        return valueFrom.StartsWith("arn:aws:ssm:", StringComparison.OrdinalIgnoreCase)
               || valueFrom.StartsWith("/", StringComparison.Ordinal)
               || valueFrom.StartsWith("parameter/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetHeader(IReadOnlyDictionary<string, string?>? headers, string headerName)
    {
        if (headers is null || headers.Count == 0)
        {
            return null;
        }

        foreach (var pair in headers)
        {
            if (string.Equals(pair.Key, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private sealed record CachedApiKeys(string ValueFrom, DateTimeOffset ExpiresAt, IReadOnlyList<string> Keys);
}

public sealed class ApiGatewayAuthorizerRequest
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string?>? Headers { get; init; }
}

public sealed class ApiGatewayAuthorizerResponse
{
    [JsonPropertyName("isAuthorized")]
    public bool IsAuthorized { get; init; }

    [JsonPropertyName("context")]
    public Dictionary<string, string> Context { get; init; } = new(StringComparer.Ordinal);

    public static ApiGatewayAuthorizerResponse Authorized(string authScheme)
    {
        return new ApiGatewayAuthorizerResponse
        {
            IsAuthorized = true,
            Context = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auth_scheme"] = authScheme
            }
        };
    }

    public static ApiGatewayAuthorizerResponse Unauthorized(string reason)
    {
        return new ApiGatewayAuthorizerResponse
        {
            IsAuthorized = false,
            Context = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["reason"] = reason
            }
        };
    }
}
