using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

/// <summary>Provisioning this machine as a builder of its own, with no SDK to download: the folders, the host's own dotnet, and a look at what a person installs.</summary>
public sealed class ProvisioningTests
{
    private static SshBuilder Builder()
    {
        var programs = new Programs();
        var options = Options.Create(new BuildOptions());

        return new SshBuilder(programs, new GitCopies(programs), new RemoteShell(options), options, TimeProvider.System);
    }

    [Fact]
    public async Task ProvisionAsync_NoSdkAsked_MakesTheFoldersAndReportsEveryStep()
    {
        var host = new Host("here", "localhost", TestFolder.Create());
        string log = Path.Combine(TestFolder.Create(), "provision.log");
        var manifest = new BuilderManifest(string.Empty, [], []);

        var answer = await Builder().ProvisionAsync(host, manifest, log, TimeSpan.FromSeconds(60), CancellationToken.None);

        var provisioned = Assert.IsType<Provisioned>(answer.Value);
        Assert.True(Directory.Exists(Path.Combine(host.Root, "workspaces")));
        Assert.True(Directory.Exists(Path.Combine(host.Root, "feeds")));
        Assert.Equal(["folders", "dotnet", "xcode", "docker", "sleep"], provisioned.Steps.Select(step => step.Name));
        Assert.True(provisioned.Steps[0].IsDone);
        Assert.True(provisioned.Steps[1].IsDone, provisioned.Steps[1].Note);
        Assert.Equal(log, provisioned.Log);
    }

    [Fact]
    public void Steps_LinesAmongInstallerOutput_AreReadInOrderWithTheirNotes()
    {
        const string log = """
            Installing workload manifest...
            s3l-step folders 1 /Users/me/.s3l-build
            s3l-step dotnet 0 dotnet-install.sh did not leave 10.0.302 in /Users/me/.s3l-build/dotnet
            noise
            s3l-step xcode 1 Xcode 27.1
            """;

        var steps = SshBuilder.Steps(log);

        Assert.Equal(3, steps.Count);
        Assert.Equal(new ProvisionStep("dotnet", false, "dotnet-install.sh did not leave 10.0.302 in /Users/me/.s3l-build/dotnet"), steps[1]);
        Assert.Equal(new ProvisionStep("xcode", true, "Xcode 27.1"), steps[2]);
    }

    [Fact]
    public void ProvisionScript_ManifestWithSdkAndWorkloads_NamesThemForTheHost()
    {
        var script = SshBuilder.ProvisionScript(new Host("mini", "mini"), new BuilderManifest("10.0.302", ["maui-ios", "wasm-tools"], ["LocalPackages"]));

        Assert.Contains("SDK='10.0.302'", script, StringComparison.Ordinal);
        Assert.Contains("WORKLOADS='maui-ios wasm-tools'", script, StringComparison.Ordinal);
        Assert.Contains("FEEDS='LocalPackages'", script, StringComparison.Ordinal);
        Assert.Contains("ROOT=\"$HOME/.s3l-build\"", script, StringComparison.Ordinal);
    }
}
