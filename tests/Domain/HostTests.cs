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

    [Fact]
    public void Needs_EveryKind_NamesACapability()
    {
        foreach (var kind in Enum.GetValues<BuildKind>())
            Assert.True(Enum.IsDefined(kind.Needs()));
    }
}
