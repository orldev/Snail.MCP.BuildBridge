using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;

namespace Snail.MCP.BuildBridge.Tests.Application;

public sealed class HostsTests
{
    private static readonly Host Mini = new("mini", "mini.local");

    private static readonly Host Studio = new("studio", "studio.local");

    private static Hosts Of(FakeBuilder builder, params Host[] hosts)
    {
        var settings = hosts.Select(host => new HostSetting { Name = host.Name, Address = host.Address, Root = host.Root });

        return new Hosts(Options.Create(new BuildOptions { Hosts = [.. settings] }), builder);
    }

    /// <summary>A host that built a moment ago can still build: its answer is kept for a minute, forgotten when something on it changes, and the list of hosts always asks anew.</summary>
    [Fact]
    public async Task ReadyAsync_AHostProbedAMomentAgo_IsNotProbedAgainUntilItIsForgotten()
    {
        var builder = new FakeBuilder();
        builder.Up[Mini.Name] = new HostState(Mini, true, [], 400, null);
        var hosts = Of(builder, Mini);

        await hosts.ReadyAsync(null, [], CancellationToken.None);
        await hosts.ReadyAsync(null, [], CancellationToken.None);
        Assert.Single(builder.Probed);

        hosts.Forget(Mini);
        await hosts.ReadyAsync(null, [], CancellationToken.None);
        await hosts.ProbeAllAsync(CancellationToken.None);

        Assert.Equal(3, builder.Probed.Count);
    }

    [Fact]
    public async Task ReadyAsync_AHostThatWasDown_IsAskedAgainEveryTime()
    {
        var builder = new FakeBuilder();
        var hosts = Of(builder, Mini);

        await hosts.ReadyAsync(null, [], CancellationToken.None);
        await hosts.ReadyAsync(null, [], CancellationToken.None);

        Assert.Equal(2, builder.Probed.Count);
    }

    [Fact]
    public void Named_NoHostsConfigured_SaysWhereTheyAreNamed()
    {
        var answer = Of(new FakeBuilder()).Named(null);

        Assert.Null(answer.Value);
        Assert.Equal(Messages.NoHosts, answer.Error);
        Assert.Equal(Messages.NoHostsHint, answer.Hint);
        Assert.Equal("noHosts", answer.Type);
    }

    [Fact]
    public void Named_NoName_IsTheFirstHost()
    {
        Assert.Equal(Mini, Of(new FakeBuilder(), Mini, Studio).Named(" ").Value);
    }

    [Fact]
    public void Named_UnknownName_ListsTheHostsThereAre()
    {
        var answer = Of(new FakeBuilder(), Mini, Studio).Named("laptop");

        Assert.Contains("mini, studio", answer.Error, StringComparison.Ordinal);
        Assert.Equal("noSuchHost", answer.Type);
    }

    [Fact]
    public void Named_AnyCase_FindsTheHost()
    {
        Assert.Equal(Studio, Of(new FakeBuilder(), Mini, Studio).Named("STUDIO").Value);
    }

    [Fact]
    public async Task ReadyAsync_HostDown_SaysSoWithHowToReachIt()
    {
        var answer = await Of(new FakeBuilder(), Mini).ReadyAsync(null, [], CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Contains("mini is down", answer.Error, StringComparison.Ordinal);
        Assert.Equal(Messages.HostDownHint, answer.Hint);
        Assert.Equal("unreachable", answer.Type);
    }

    [Fact]
    public async Task ReadyAsync_HostWithoutTheToolchain_IsRefusedForThatKind()
    {
        var builder = new FakeBuilder();
        builder.Up[Mini.Name] = new HostState(Mini, true, [new Toolchain(Capability.Dotnet, "10.0.302"), new Toolchain(Capability.Xcode, null)], 400, null);

        var dotnet = await Of(builder, Mini).ReadyAsync("mini", [Capability.Dotnet], CancellationToken.None);
        var xcode = await Of(builder, Mini).ReadyAsync("mini", [Capability.Xcode], CancellationToken.None);

        Assert.NotNull(dotnet.Value);
        Assert.Null(xcode.Value);
        Assert.Contains("no xcode", xcode.Error, StringComparison.Ordinal);
        Assert.Equal("noToolchain", xcode.Type);
    }

    /// <summary>An Android build takes the toolchain under the root or docker: a host with either takes it, one with neither is told of both.</summary>
    [Fact]
    public async Task ReadyAsync_KindWithAlternatives_TakesAHostWithAnyOfThemAndNamesBothWhenNone()
    {
        var builder = new FakeBuilder();
        builder.Up[Mini.Name] = new HostState(Mini, true, [new Toolchain(Capability.Docker, "29.0"), new Toolchain(Capability.Android, null)], 400, null);

        var withDocker = await Of(builder, Mini).ReadyAsync("mini", [Capability.Android, Capability.Docker], CancellationToken.None);
        var withNeither = await Of(builder, Mini).ReadyAsync("mini", [Capability.Android, Capability.Xcode], CancellationToken.None);

        Assert.NotNull(withDocker.Value);
        Assert.Null(withNeither.Value);
        Assert.Contains("no android or xcode", withNeither.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeAllAsync_EveryHost_IsProbedOnce()
    {
        var builder = new FakeBuilder();

        var states = await Of(builder, Mini, Studio).ProbeAllAsync(CancellationToken.None);

        Assert.Equal([Mini, Studio], builder.Probed);
        Assert.Equal(2, states.Count);
    }
}
