using System.Text.Json;

namespace ControlPlane.Api.Auth;

public static class ApiKeyConfiguration
{
    public static IReadOnlyList<string> GetConfiguredApiKeys(IConfiguration configuration)
    {
        var keys = configuration
            .GetSection("ApiSecurity:ApiKeys")
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToList();

        var singleKey = configuration["ApiSecurity:ApiKey"];
        if (!string.IsNullOrWhiteSpace(singleKey))
        {
            var normalized = singleKey.Trim();

            // Support injecting a Secrets Manager JSON array through ApiSecurity__ApiKey.
            if (TryParseJsonArray(normalized, out var parsedKeys))
            {
                keys.AddRange(parsedKeys);
            }
            else
            {
                keys.Add(normalized);
            }
        }

        return keys
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryParseJsonArray(string value, out IReadOnlyList<string> keys)
    {
        keys = Array.Empty<string>();
        if (!value.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(value);
            if (parsed is null)
            {
                return false;
            }

            keys = parsed
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray();

            return keys.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
