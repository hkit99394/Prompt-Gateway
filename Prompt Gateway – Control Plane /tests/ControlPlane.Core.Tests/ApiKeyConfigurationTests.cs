using ControlPlane.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace ControlPlane.Core.Tests;

public class ApiKeyConfigurationTests
{
    [Test]
    public void GetConfiguredApiKeys_WhenSingleKeyContainsJsonArray_ReturnsArrayItems()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiSecurity:ApiKey"] = "[\"key-one\",\"key-two\"]"
            })
            .Build();

        var keys = ApiKeyConfiguration.GetConfiguredApiKeys(configuration);

        Assert.That(keys, Is.EquivalentTo(new[] { "key-one", "key-two" }));
    }
}
