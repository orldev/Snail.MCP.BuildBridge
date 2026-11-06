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

        var shell = new RemoteShell(options);

        return new SshBuilder(programs, new RsyncCourier(programs, new GitCopies(programs), shell, options, TimeProvider.System), shell, options, TimeProvider.System);
    }

    [Fact]
    public async Task ProvisionAsync_NoSdkAsked_MakesTheFoldersAndReportsEveryStep()
    {
        var host = new Host("here", "localhost", TestFolder.Create());
        string log = Path.Combine(TestFolder.Create(), "provision.log");
        var manifest = new BuilderManifest(string.Empty, [], [], [], false) { HasAndroid = false, HasNode = false };

        var answer = await Builder().ProvisionAsync(host, manifest, log, TimeSpan.FromSeconds(60), CancellationToken.None);

        var provisioned = Assert.IsType<Provisioned>(answer.Value);
        Assert.True(Directory.Exists(Path.Combine(host.Root, "workspaces")));
        Assert.True(Directory.Exists(Path.Combine(host.Root, "feeds")));
        Assert.Equal(["folders", "dotnet", "xcode", "docker", "sleep", "rosetta"], provisioned.Steps.Select(step => step.Name));
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

        var steps = ProvisionScripts.Steps(log);

        Assert.Equal(3, steps.Count);
        Assert.Equal(new ProvisionStep("dotnet", false, "dotnet-install.sh did not leave 10.0.302 in /Users/me/.s3l-build/dotnet"), steps[1]);
        Assert.Equal(new ProvisionStep("xcode", true, "Xcode 27.1"), steps[2]);
    }

    [Fact]
    public void ProvisionScript_ManifestWithSdkAndWorkloads_NamesThemForTheHost()
    {
        var script = ProvisionScripts.Of(new Host("mini", "mini"), new BuilderManifest("10.0.302", ["maui-ios", "wasm-tools"], ["LocalPackages"], ["dotnet"]));

        Assert.Contains("SDK='10.0.302'", script, StringComparison.Ordinal);
        Assert.Contains("WORKLOADS='maui-ios wasm-tools'", script, StringComparison.Ordinal);
        Assert.Contains("FEEDS='LocalPackages'", script, StringComparison.Ordinal);
        Assert.Contains("ROOT=\"$HOME/.s3l-build\"", script, StringComparison.Ordinal);
        Assert.Contains("--label snail.dockerfile=", script, StringComparison.Ordinal);
        Assert.Contains("s3l-step xcodegen", script, StringComparison.Ordinal);
        Assert.Contains("s3l-step rosetta", script, StringComparison.Ordinal);
        Assert.Contains("releases/download/2.46.0/xcodegen.zip", script, StringComparison.Ordinal);
        Assert.Contains(BuilderManifest.DefaultXcodeGenSha256, script, StringComparison.Ordinal);
        Assert.Contains("'s3l-build-dotnet:10.0.302'", script, StringComparison.Ordinal);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:${SDK}", script, StringComparison.Ordinal);
        Assert.Contains("""<add key="LocalPackages" value="/feeds/LocalPackages" />""", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_ManifestWithAndroid_FetchesTheJdkAndTheToolsByChecksumAndNamesThePackages()
    {
        var manifest = new BuilderManifest("10.0.302", [], [], []) { AndroidPackages = ["platform-tools", "ndk;28.2.13676358"] };

        var script = ProvisionScripts.Of(new Host("mini", "mini"), manifest);

        Assert.Contains("jdk-21.0.13%2B9/OpenJDK21U-jdk_aarch64_mac_hotspot_21.0.13_9.tar.gz", script, StringComparison.Ordinal);
        Assert.Contains(BuilderManifest.DefaultJdkSha256, script, StringComparison.Ordinal);
        Assert.Contains("commandlinetools-mac-11076708_latest.zip", script, StringComparison.Ordinal);
        Assert.Contains(BuilderManifest.DefaultAndroidToolsSha256, script, StringComparison.Ordinal);
        Assert.Contains("PACKAGES='platform-tools ndk;28.2.13676358'", script, StringComparison.Ordinal);
        Assert.Contains("--licenses", script, StringComparison.Ordinal);
        Assert.Contains("s3l-step jdk", script, StringComparison.Ordinal);
        Assert.Contains("s3l-step android", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_ManifestWithNode_FetchesTheReleaseByChecksum()
    {
        var script = ProvisionScripts.Of(new Host("mini", "mini"), new BuilderManifest("10.0.302", [], [], []));

        Assert.Contains("https://nodejs.org/dist/v26.13.0/node-v26.13.0-darwin-arm64.tar.gz", script, StringComparison.Ordinal);
        Assert.Contains(BuilderManifest.DefaultNodeSha256, script, StringComparison.Ordinal);
        Assert.Contains("s3l-step node", script, StringComparison.Ordinal);
        Assert.DoesNotContain("s3l-step node", ProvisionScripts.Of(new Host("mini", "mini"), new BuilderManifest("10.0.302", [], [], []) { HasNode = false }), StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_ManifestWithoutAndroid_LeavesTheToolchainOut()
    {
        var script = ProvisionScripts.Of(new Host("mini", "mini"), new BuilderManifest("10.0.302", [], [], []) { HasAndroid = false });

        Assert.DoesNotContain("s3l-step jdk", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sdkmanager", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("21.0.13+9", "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jdk_aarch64_mac_hotspot_21.0.13_9.tar.gz")]
    [InlineData("17.0.17+10", "https://github.com/adoptium/temurin17-binaries/releases/download/jdk-17.0.17%2B10/OpenJDK17U-jdk_aarch64_mac_hotspot_17.0.17_10.tar.gz")]
    public void JdkUrlOf_AVersionAsAdoptiumNamesIt_IsTheReleaseAssetForMacArm64(string version, string expected) =>
        Assert.Equal(expected, BuilderManifests.JdkUrlOf(version));

    [Fact]
    public void ImageScript_Android_BuildsForAmd64UnderItsOwnLabel()
    {
        string script = ProvisionScripts.ImageScript(BuildImage.Android, "10.0.302");

        Assert.Contains("--platform linux/amd64", script, StringComparison.Ordinal);
        Assert.Contains("--label snail.image=buildbridge", script, StringComparison.Ordinal);
        Assert.Contains("maui-android", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageScript_ADockerfileChanged_IsBuiltAgainUnderAnotherHash()
    {
        string one = ProvisionScripts.DockerfileHash("FROM a\n");
        string other = ProvisionScripts.DockerfileHash("FROM a\nRUN b\n");

        Assert.NotEqual(one, other);
        Assert.Equal(one, ProvisionScripts.DockerfileHash("FROM a"));
    }

    [Fact]
    public void ImageScript_UnknownImage_ReportsAStepNotDone()
    {
        string script = ProvisionScripts.ImageScript("rust", "10.0.302");

        Assert.Contains("s3l-step image-rust 0", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Steps_ImageLines_AreReadBack()
    {
        var steps = ProvisionScripts.Steps("s3l-step image-dotnet 1 s3l-build-dotnet:10.0.302 built\ns3l-step image-android 0 docker is not there to build s3l-build-android:10.0.302\n");

        Assert.Equal(new ProvisionStep("image-dotnet", true, "s3l-build-dotnet:10.0.302 built"), steps[0]);
        Assert.False(steps[1].IsDone);
    }
}
