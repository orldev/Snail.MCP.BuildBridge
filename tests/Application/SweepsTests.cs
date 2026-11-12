using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Application;

public sealed class SweepsTests
{
    private static readonly Host Mini = new("mini", "mini");

    private static (Sweeps Sweeps, FakeBuilder Builder) Of()
    {
        var builder = new FakeBuilder();
        builder.Found = new Survey(Mini, [new WorkspaceState(new WorkspaceKey("Sample", "old"), DateTimeOffset.UtcNow.AddDays(-30), 10)], [], [], 0, 0, 400);

        return (new Sweeps(builder, new Logs(TimeProvider.System, TestFolder.Create()), Options.Create(new BuildOptions()), TimeProvider.System), builder);
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

    /// <summary>A session whose copy is gone from this machine holds a connection to the host for nothing: the sweep after a run ends it, and a caches plan shows it first.</summary>
    [Fact]
    public async Task AfterRunAsync_SessionsWhoseCopyIsGone_AreEndedAndACachesPlanNamesThem()
    {
        var (sweeps, builder) = Of();
        builder.Stranded.Add("s3l-sample-gone-mini");

        var plan = (await sweeps.PlanAsync(Mini, SweepScope.Caches, null, null, CancellationToken.None)).Value!;
        var after = await sweeps.AfterRunAsync(Mini, "Other", CancellationToken.None);

        Assert.Contains(plan.Items, item => item is { Kind: SweepKind.Session, Name: "s3l-sample-gone-mini" });
        Assert.Equal(SweepKind.Session, Assert.Single(after).Item.Kind);
        Assert.Contains(builder.CarriedOut, item => item.Name == "s3l-sample-gone-mini");
    }

    /// <summary>A session reconnects whenever the host's sshd restarts, and fifty of them at once are turned away: the sweep after a run pauses those of workspaces left alone, and says so without removing anything.</summary>
    [Fact]
    public async Task AfterRunAsync_AWorkspaceLeftAlone_HasItsSessionPausedAndSaysSo()
    {
        var (sweeps, builder) = Of();
        builder.Paused.Add("s3l-sample-old-mini");

        var after = await sweeps.AfterRunAsync(Mini, "Other", CancellationToken.None);

        var rested = Assert.Single(after);
        Assert.Equal([new WorkspaceKey("Sample", "old")], builder.Idle);
        Assert.Equal((SweepKind.Session, "s3l-sample-old-mini", true), (rested.Item.Kind, rested.Item.Name, rested.IsDone));
        Assert.Contains("paused", rested.Note, StringComparison.Ordinal);
        Assert.Empty(builder.CarriedOut);
    }

    /// <summary>A workspace the sweep removes takes its session with it, so that session is not asked to rest as well.</summary>
    [Fact]
    public async Task AfterRunAsync_AStaleWorkspaceOfTheRepository_IsRemovedAndNotAskedToRest()
    {
        var (sweeps, builder) = Of();

        await sweeps.AfterRunAsync(Mini, "Sample", CancellationToken.None);

        Assert.Equal(["Sample/old"], builder.CarriedOut.Select(item => item.Name));
        Assert.Empty(builder.Idle);
    }

    /// <summary>Surveying the host after every run was most of a short run: within the minutes the settings say a second run of the repository ends without one, and the survey a sweep after a run asks for measures no sizes.</summary>
    [Fact]
    public async Task AfterRunAsync_ASecondRunWithinTheMinutes_SurveysNothingAndTheFirstAsksForNoSizes()
    {
        var (sweeps, builder) = Of();

        await sweeps.AfterRunAsync(Mini, "Sample", CancellationToken.None);
        var again = await sweeps.AfterRunAsync(Mini, "Sample", CancellationToken.None);
        await sweeps.AfterRunAsync(Mini, "Other", CancellationToken.None);

        Assert.Empty(again);
        Assert.Equal(2, builder.Surveys);
        Assert.False(builder.IsLastSurveySized);
    }

    /// <summary>The caches scope ran only when asked for or on a host short of room, so a repository nobody builds any more kept its workspace: once a day the sweep after a run of any repository is that whole scope.</summary>
    [Fact]
    public async Task AfterRunAsync_ADaySinceTheWholeSweep_TakesTheStaleWorkspacesOfEveryRepository()
    {
        var builder = new FakeBuilder();
        builder.Found = new Survey(Mini, [new WorkspaceState(new WorkspaceKey("Sample", "old"), DateTimeOffset.UtcNow.AddDays(-30), 10)], [], [], 0, 0, 400);
        var logs = new Logs(TimeProvider.System, TestFolder.Create());
        var sweeps = new Sweeps(builder, logs, Options.Create(new BuildOptions()), TimeProvider.System);

        await sweeps.AfterRunAsync(Mini, "Other", CancellationToken.None);
        Assert.Empty(builder.CarriedOut);
        File.SetLastWriteTimeUtc(Path.Combine(logs.Folder, ".swept-caches-mini"), DateTime.UtcNow.AddHours(-25));
        var swept = await sweeps.AfterRunAsync(Mini, "Other", CancellationToken.None);

        Assert.Equal(["Sample/old"], builder.CarriedOut.Select(item => item.Name));
        Assert.Equal(SweepKind.Workspace, Assert.Single(swept).Item.Kind);
    }

    [Fact]
    public async Task AfterRunAsync_TheMinutesSetToZero_SweepsAfterEveryRun()
    {
        var builder = new FakeBuilder();
        var sweeps = new Sweeps(builder, new Logs(TimeProvider.System, TestFolder.Create()), Options.Create(new BuildOptions { SweepMinutes = 0 }), TimeProvider.System);

        await sweeps.AfterRunAsync(Mini, "Sample", CancellationToken.None);
        await sweeps.AfterRunAsync(Mini, "Sample", CancellationToken.None);

        Assert.Equal(2, builder.Surveys);
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
        Assert.Equal("noSuchPlan", again.Type);
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

    [Fact]
    public void ScopeOf_UnknownWord_IsRefusedAsArguments()
    {
        var answer = Sweeps.ScopeOf("all");

        Assert.Contains("is not a scope", answer.Error, StringComparison.Ordinal);
        Assert.Equal(ToolFailures.Arguments, answer.Type);
    }
}
