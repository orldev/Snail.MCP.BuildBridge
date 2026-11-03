using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;

namespace Snail.MCP.BuildBridge.Tests.Application;

public sealed class SweepsTests
{
    private static readonly Host Mini = new("mini", "mini");

    private static (Sweeps Sweeps, FakeBuilder Builder) Of()
    {
        var builder = new FakeBuilder();
        builder.Found = new Survey(Mini, [new WorkspaceState(new WorkspaceKey("Sample", "old"), DateTimeOffset.UtcNow.AddDays(-30), 10)], [], [], 0, 0, 400);

        return (new Sweeps(builder, new Logs(TimeProvider.System), Options.Create(new BuildOptions()), TimeProvider.System), builder);
    }

    [Fact]
    public async Task ApplyAsync_APlanShown_CarriesOutExactlyItsItems()
    {
        var (sweeps, builder) = Of();
        var plan = (await sweeps.PlanAsync(Mini, SweepScope.Caches, null, null, CancellationToken.None)).Value!;

        var results = (await sweeps.ApplyAsync(plan.Id, CancellationToken.None)).Value!;

        Assert.Equal(["Sample/old"], builder.CarriedOut.Select(item => item.Name));
        Assert.All(results, result => Assert.True(result.IsDone));
    }

    [Fact]
    public async Task ApplyAsync_APlanTwice_IsRefusedTheSecondTime()
    {
        var (sweeps, _) = Of();
        var plan = (await sweeps.PlanAsync(Mini, SweepScope.Caches, null, null, CancellationToken.None)).Value!;
        await sweeps.ApplyAsync(plan.Id, CancellationToken.None);

        var again = await sweeps.ApplyAsync(plan.Id, CancellationToken.None);

        Assert.Null(again.Value);
        Assert.Contains("No plan is", again.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_UnknownPlan_SaysSo()
    {
        var (sweeps, builder) = Of();

        var answer = await sweeps.ApplyAsync("nope", CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Empty(builder.CarriedOut);
    }

    [Fact]
    public async Task BeforeRunAsync_RoomEnough_SweepsNothing()
    {
        var (sweeps, builder) = Of();

        var results = await sweeps.BeforeRunAsync(new HostState(Mini, true, [], 100, null), CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(builder.CarriedOut);
    }

    [Theory]
    [InlineData("caches", SweepScope.Caches)]
    [InlineData("Everything", SweepScope.Everything)]
    public void ScopeOf_AnyCase_IsTheScope(string word, SweepScope expected) => Assert.Equal(expected, Sweeps.ScopeOf(word).Value);
}
