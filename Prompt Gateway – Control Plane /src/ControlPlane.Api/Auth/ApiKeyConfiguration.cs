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
            keys.Add(singleKey.Trim());
        }

        return keys
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
