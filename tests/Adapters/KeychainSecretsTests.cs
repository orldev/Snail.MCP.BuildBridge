using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Configuration;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

public sealed class KeychainSecretsTests
{
    private static KeychainSecrets Of(params SecretSetting[] secrets) => new(new Programs(), Options.Create(new BuildOptions { Secrets = secrets }));

    /// <summary>A secret of the settings file is the run's as it is, and only for the repository it is scoped to.</summary>
    [Fact]
    public async Task ForAsync_ValuesInTheSettings_AreTheRepositorysOnly()
    {
        var secrets = Of(new SecretSetting { Name = "TOKEN", Value = "t0ken", Repository = "Anonym" }, new SecretSetting { Name = "SHARED", Value = "s" });

        var anonym = await secrets.ForAsync("anonym", CancellationToken.None);
        var other = await secrets.ForAsync("Other", CancellationToken.None);

        Assert.Equal(new Dictionary<string, string> { ["TOKEN"] = "t0ken", ["SHARED"] = "s" }, anonym.Value);
        Assert.Equal(new Dictionary<string, string> { ["SHARED"] = "s" }, other.Value);
    }

    /// <summary>A keychain service this machine does not hold fails the run before anything reaches the host, with the command that adds it.</summary>
    [Fact]
    public async Task ForAsync_AKeychainServiceMissing_SaysWhichAndHowToAddIt()
    {
        var secrets = Of(new SecretSetting { Name = "TOKEN", Keychain = $"s3l-test-{Path.GetRandomFileName()[..6]}" });

        var answer = await secrets.ForAsync("Any", CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Contains("TOKEN", answer.Error, StringComparison.Ordinal);
        Assert.Contains("security add-generic-password", answer.Hint, StringComparison.Ordinal);
    }
}
