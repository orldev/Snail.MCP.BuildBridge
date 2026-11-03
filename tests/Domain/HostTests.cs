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
        Assert.Equal(Capability.Docker, BuildKind.Android.Needs());
    }

    [Fact]
    public void Needs_EveryKind_NamesACapability()
    {
        foreach (var kind in Enum.GetValues<BuildKind>())
            Assert.True(kind.Needs() is not { } capability || Enum.IsDefined(capability));
    }
}
