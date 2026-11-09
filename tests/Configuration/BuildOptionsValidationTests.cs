using System.Collections;
using Microsoft.Extensions.Configuration;
using Snail.MCP.BuildBridge.Configuration;

namespace Snail.MCP.BuildBridge.Tests.Configuration;

/// <summary>The settings refused at start, each for a reason said in words that name the variable the operator sets.</summary>
public sealed class BuildOptionsValidationTests
{
    [Fact]
    public void ProblemsOf_TheDefaults_AreNone() => Assert.Empty(BuildOptionsValidation.ProblemsOf(new BuildOptions()));

    [Fact]
    public void ProblemsOf_TwoHostsOfOneNameAndOneWithoutAnAddress_NamesBothByTheirVariables()
    {
        var options = new BuildOptions { Hosts = [new HostSetting { Name = "mini", Address = "dev@mini" }, new HostSetting { Name = "Mini" }] };

        var problems = BuildOptionsValidation.ProblemsOf(options);

        Assert.Equal(
            [
                "SNAIL_MCP_BUILDBRIDGE_HOSTS__1__ADDRESS (or Hosts:1:Address in the settings file) is empty; it is what ssh is given for the host Mini: an alias from the SSH configuration, or user@host.",
                "SNAIL_MCP_BUILDBRIDGE_HOSTS__1__NAME (or Hosts:1:Name in the settings file) is 'Mini'; it is a name no other host has, case aside, and Hosts:0:Name is 'mini'."
            ],
            problems);
    }

    [Fact]
    public void ProblemsOf_AHostWithoutANameOrARoot_NamesEachVariable()
    {
        var options = new BuildOptions { Hosts = [new HostSetting { Address = "dev@mini", Root = "" }] };

        var problems = BuildOptionsValidation.ProblemsOf(options);

        Assert.Equal(2, problems.Count);
        Assert.StartsWith("SNAIL_MCP_BUILDBRIDGE_HOSTS__0__NAME (or Hosts:0:Name in the settings file) is empty;", problems[0], StringComparison.Ordinal);
        Assert.StartsWith("SNAIL_MCP_BUILDBRIDGE_HOSTS__0__ROOT (or Hosts:0:Root in the settings file) is empty;", problems[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 3600, 20, "SNAIL_MCP_BUILDBRIDGE_TRANSFER_SECONDS (or TransferSeconds in the settings file) is '0'; it is a number of seconds above zero.")]
    [InlineData(600, -1, 20, "SNAIL_MCP_BUILDBRIDGE_RUN_SECONDS (or RunSeconds in the settings file) is '-1'; it is a number of seconds above zero.")]
    [InlineData(600, 3600, 0, "SNAIL_MCP_BUILDBRIDGE_PROBE_SECONDS (or ProbeSeconds in the settings file) is '0'; it is a number of seconds above zero.")]
    public void ProblemsOf_ATimeOfZero_IsRefusedNamingItsVariable(int transfer, int run, int probe, string expected) =>
        Assert.Equal(expected, Assert.Single(BuildOptionsValidation.ProblemsOf(new BuildOptions { TransferSeconds = transfer, RunSeconds = run, ProbeSeconds = probe })));

    [Fact]
    public void ProblemsOf_ThreeTimesOfZero_AreThreeSentences() =>
        Assert.Equal(3, BuildOptionsValidation.ProblemsOf(new BuildOptions { TransferSeconds = 0, RunSeconds = 0, ProbeSeconds = 0 }).Count);

    [Fact]
    public void ProblemsOf_AnUnknownSync_IsRefusedNamingItsVariable() =>
        Assert.Equal(
            "SNAIL_MCP_BUILDBRIDGE_SYNC (or Sync in the settings file) is 'scp'; it is rsync or mutagen.",
            Assert.Single(BuildOptionsValidation.ProblemsOf(new BuildOptions { Sync = "scp" })));

    /// <summary>A secret is a variable's name with one source: no name, two sources or none is refused at start, each said, and the value is never said back.</summary>
    [Fact]
    public void ProblemsOf_SecretsWithoutANameOrWithTwoSourcesOrNone_AreRefusedEach()
    {
        var options = new BuildOptions
        {
            Secrets =
            [
                new SecretSetting { Name = "TOKEN", Keychain = "anonym" },
                new SecretSetting { Name = "1BAD", Value = "x" },
                new SecretSetting { Name = "BOTH", Value = "hidden-value", Keychain = "y" },
                new SecretSetting { Name = "NONE" }
            ]
        };

        var problems = BuildOptionsValidation.ProblemsOf(options);

        Assert.Equal(3, problems.Count);
        Assert.StartsWith("SNAIL_MCP_BUILDBRIDGE_SECRETS__1__NAME (or Secrets:1:Name in the settings file) is '1BAD';", problems[0], StringComparison.Ordinal);
        Assert.StartsWith("SNAIL_MCP_BUILDBRIDGE_SECRETS__2__VALUE (or Secrets:2:Value in the settings file) is set and SNAIL_MCP_BUILDBRIDGE_SECRETS__2__KEYCHAIN (or Secrets:2:Keychain in the settings file) is 'y' for the secret BOTH;", problems[1], StringComparison.Ordinal);
        Assert.StartsWith("SNAIL_MCP_BUILDBRIDGE_SECRETS__3__VALUE (or Secrets:3:Value in the settings file) and SNAIL_MCP_BUILDBRIDGE_SECRETS__3__KEYCHAIN (or Secrets:3:Keychain in the settings file) are both empty for the secret NONE;", problems[2], StringComparison.Ordinal);
        Assert.DoesNotContain(problems, problem => problem.Contains("hidden-value", StringComparison.Ordinal));
    }

    /// <summary>The variables the problems name are the ones the settings are bound from, a host's under its index too.</summary>
    [Fact]
    public void ProblemsOf_TheVariablesNamed_AreTheOnesTheSettingsBindFrom()
    {
        var variables = new Hashtable
        {
            ["SNAIL_MCP_BUILDBRIDGE_RUN_SECONDS"] = "0",
            ["SNAIL_MCP_BUILDBRIDGE_HOSTS__0__NAME"] = "mini",
            ["SNAIL_MCP_BUILDBRIDGE_HOSTS__0__ROOT"] = "/opt/build"
        };

        var options = BuildSettings.Read(null, variables).Get<BuildOptions>()!;

        Assert.Equal(0, options.RunSeconds);
        Assert.Equal("mini", Assert.Single(options.Hosts).Name);
        Assert.Equal("/opt/build", options.Hosts[0].Root);
        Assert.Equal(
            [
                "SNAIL_MCP_BUILDBRIDGE_HOSTS__0__ADDRESS (or Hosts:0:Address in the settings file) is empty; it is what ssh is given for the host mini: an alias from the SSH configuration, or user@host.",
                "SNAIL_MCP_BUILDBRIDGE_RUN_SECONDS (or RunSeconds in the settings file) is '0'; it is a number of seconds above zero."
            ],
            BuildOptionsValidation.ProblemsOf(options));
    }
}
