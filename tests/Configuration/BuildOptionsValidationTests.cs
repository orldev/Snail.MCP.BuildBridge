using Snail.MCP.BuildBridge.Configuration;

namespace Snail.MCP.BuildBridge.Tests.Configuration;

/// <summary>The settings refused at start, each for a reason said in words.</summary>
public sealed class BuildOptionsValidationTests
{
    [Fact]
    public void ProblemsOf_TheDefaults_AreNone() => Assert.Empty(BuildOptionsValidation.ProblemsOf(new BuildOptions()));

    [Fact]
    public void ProblemsOf_TwoHostsOfOneNameAndOneWithoutAnAddress_NamesBoth()
    {
        var options = new BuildOptions { Hosts = [new HostSetting { Name = "mini", Address = "dev@mini" }, new HostSetting { Name = "Mini" }] };

        var problems = BuildOptionsValidation.ProblemsOf(options);

        Assert.Contains(problems, problem => problem.Contains("no Address", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Two hosts", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 3600, 20)]
    [InlineData(600, -1, 20)]
    [InlineData(600, 3600, 0)]
    public void ProblemsOf_ATimeOfZero_IsRefused(int transfer, int run, int probe) =>
        Assert.Single(BuildOptionsValidation.ProblemsOf(new BuildOptions { TransferSeconds = transfer, RunSeconds = run, ProbeSeconds = probe }));

    [Fact]
    public void ProblemsOf_AnUnknownSync_IsRefused() =>
        Assert.Single(BuildOptionsValidation.ProblemsOf(new BuildOptions { Sync = "scp" }));

    /// <summary>A secret is a variable's name with one source: no name, two sources or none is refused at start, each said.</summary>
    [Fact]
    public void ProblemsOf_SecretsWithoutANameOrWithTwoSourcesOrNone_AreRefusedEach()
    {
        var options = new BuildOptions
        {
            Secrets =
            [
                new SecretSetting { Name = "TOKEN", Keychain = "anonym" },
                new SecretSetting { Name = "1BAD", Value = "x" },
                new SecretSetting { Name = "BOTH", Value = "x", Keychain = "y" },
                new SecretSetting { Name = "NONE" }
            ]
        };

        var problems = BuildOptionsValidation.ProblemsOf(options);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("1BAD", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("BOTH", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("NONE", StringComparison.Ordinal));
    }
}
