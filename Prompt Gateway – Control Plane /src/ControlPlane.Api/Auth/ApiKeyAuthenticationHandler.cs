using System.Security.Claims;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Auth;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";

    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredApiKeys = ApiKeyConfiguration.GetConfiguredApiKeys(_configuration);
        if (configuredApiKeys.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("API keys are not configured."));
        }

        if (!Request.Headers.TryGetValue(HeaderName, out var headerValue) || string.IsNullOrWhiteSpace(headerValue))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API key."));
        }

        var providedApiKey = headerValue.ToString();
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);
        var isValid = configuredApiKeys.Any(configuredApiKey =>
        {
            var configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);
            return CryptographicOperations.FixedTimeEquals(configuredBytes, providedBytes);
        });
        if (!isValid)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "api-client") };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
