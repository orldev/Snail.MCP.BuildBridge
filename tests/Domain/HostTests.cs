namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class HostTests
{
    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("mini", false)]
    [InlineData("orldev@192.168.3.182", false)]
    public void IsLocal_ByAddress_NamesThisMachineOnly(string address, bool expected) =>
        Assert.Equal(expected, new Host("any", address).IsLocal);

    [Fact]
    public void Has_ToolchainPresent_IsTrueOnlyWithAVersion()
    {
        var host = new Host("mini", "mini");
        var state = new HostState(host, true, [new Toolchain(Capability.Dotnet, "10.0.302"), new Toolchain(Capability.Xcode, null)], 500, null);

        Assert.True(state.Has(Capability.Dotnet));
        Assert.False(state.Has(Capability.Xcode));
        Assert.False(state.Has(Capability.Docker));
    }

    [Theory]
    [InlineData("~/.s3l-build", "/Users/dev", "/Users/dev/.s3l-build")]
    [InlineData("/Volumes/Build/s3l", "/Users/dev", "/Volumes/Build/s3l")]
    public void RootOn_HomeReported_IsTheRootAsAnAbsolutePath(string root, string home, string expected) =>
        Assert.Equal(expected, new Host("mini", "mini", root).RootOn(home));

    [Fact]
    public void ImageOf_ContainerKinds_NameAnImageAndHostKindsNone()
    {
        Assert.Equal(BuildImage.Dotnet, BuildKind.Linux.ImageOf());
        Assert.Equal(BuildImage.Android, BuildKind.Android.ImageOf());
        Assert.Null(BuildKind.Dotnet.ImageOf());
        Assert.Null(BuildKind.Gradle.ImageOf());
    }

    /// <summary>An Android build prefers the toolchain under the root and falls back to the image; gradle has no image to fall back to.</summary>
    [Fact]
    public void Needs_AndroidAndGradle_AskForTheToolchainWithDockerAsTheFallbackForAndroidOnly()
    {
        Assert.Equal([Capability.Android, Capability.Docker], BuildKind.Android.Needs());
        Assert.Equal([Capability.Android], BuildKind.Gradle.Needs());
        Assert.Equal([Capability.Node], BuildKind.Npm.Needs());
        Assert.Equal("npm", BuildKind.Npm.Program(null));
        Assert.Empty(BuildKind.Script.Needs());
    }

    [Fact]
    public void Needs_EveryKind_NamesCapabilitiesThatExist()
    {
        foreach (var kind in Enum.GetValues<BuildKind>())
            Assert.All(kind.Needs(), capability => Assert.True(Enum.IsDefined(capability)));
    }

    [Fact]
    public void ImageOn_AndroidOnAHostWithTheToolchain_IsNoneAndOnOneWithoutItTheImage()
    {
        var host = new Host("mini", "mini");
        var withToolchain = new HostState(host, true, [new Toolchain(Capability.Android, "jdk 21.0.13, ndk 28.2.13676358"), new Toolchain(Capability.Docker, "29.0")], 500, null);
        var withDockerOnly = new HostState(host, true, [new Toolchain(Capability.Android, null), new Toolchain(Capability.Docker, "29.0")], 500, null);

        Assert.Null(BuildKind.Android.ImageOn(withToolchain));
        Assert.Equal(BuildImage.Android, BuildKind.Android.ImageOn(withDockerOnly));
        Assert.Equal(BuildImage.Dotnet, BuildKind.Linux.ImageOn(withToolchain));
    }

    [Fact]
    public void Program_ByKindAndImage_IsDockerInAContainerAndTheKindsOwnOnTheHost()
    {
        Assert.Equal("docker", BuildKind.Android.Program(BuildImage.Android));
        Assert.Equal("dotnet", BuildKind.Android.Program(null));
        Assert.Equal("./gradlew", BuildKind.Gradle.Program(null));
        Assert.Null(BuildKind.Script.Program(null));
    }
}
