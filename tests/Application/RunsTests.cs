using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.BuildBridge.Tests.Application;

public sealed class RunsTests
{
    private static readonly Host Mini = new("mini", "mini.local");

    private sealed class Steps : IProgress<JobProgress>
    {
        public List<string> Said { get; } = [];

        public void Report(JobProgress value) => Said.Add(value.What ?? string.Empty);
    }

    private static (Runs Runs, FakeBuilder Builder) Of(IEnumerable<Capability>? has = null, FeedSetting[]? feeds = null)
    {
        var builder = new FakeBuilder();
        var toolchains = (has ?? [Capability.Dotnet]).Select(capability => new Toolchain(capability, "1"));
        builder.Up[Mini.Name] = new HostState(Mini, true, [.. toolchains], 400, null);
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }], Feeds = feeds ?? [] });
        var hosts = new Hosts(options, builder);

        var logs = new Logs(TimeProvider.System);

        return (new Runs(hosts, new FakeCopies(), builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), options), builder);
    }

    [Fact]
    public async Task RunAsync_DotnetTest_DeliversRunsAndReadsWhatTheTestsSaid()
    {
        var (runs, builder) = Of();
        var steps = new Steps();

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test", "-c", "Release"], null, steps, CancellationToken.None);

        var report = Assert.IsType<BuildReport>(answer.Value);
        Assert.True(report.IsSuccess);
        Assert.Equal(["dotnet", "test", "-c", "Release"], Assert.Single(builder.Executed).Command);
        Assert.Equal(new TestSummary(12, 0, 1, 13), report.Tests);
        Assert.Single(builder.Delivered);
        Assert.Contains(steps.Said, step => step.StartsWith("running dotnet test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_FeedsConfigured_AreMirroredBeforeTheRun()
    {
        var (runs, builder) = Of(feeds: [new FeedSetting { Name = "LocalPackages", Path = "/Users/me/nuget" }]);

        await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], null, new Steps(), CancellationToken.None);

        Assert.Equal(("/Users/me/nuget", "feeds/LocalPackages"), Assert.Single(builder.Mirrored));
    }

    [Fact]
    public async Task RunAsync_HostWithoutTheToolchain_RunsNothing()
    {
        var (runs, builder) = Of(has: [Capability.Dotnet]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Xcode, ["build"], null, new Steps(), CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Contains("no xcode", answer.Error, StringComparison.Ordinal);
        Assert.Empty(builder.Delivered);
    }

    [Fact]
    public async Task RunAsync_FailedTests_IsNotASuccessEvenWithExitCodeZero()
    {
        var (runs, builder) = Of();
        builder.LogText = "Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 1 s - A.dll (net10.0)\n";

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test"], null, new Steps(), CancellationToken.None);

        Assert.False(answer.Value!.IsSuccess);
    }

    /// <summary>Two runs on one workspace go one after the other, the second saying it waits.</summary>
    [Fact]
    public async Task RunAsync_SameWorkspaceTwice_TakesTurns()
    {
        var (runs, builder) = Of();
        var gate = new TaskCompletionSource();
        builder.Held = gate.Task;
        var second = new Steps();

        var first = runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], null, new Steps(), CancellationToken.None);
        await Task.Delay(100);
        var waiting = runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test"], null, second, CancellationToken.None);
        await Task.Delay(100);

        Assert.Single(builder.Executed);
        Assert.Contains(second.Said, step => step.StartsWith("waiting for", StringComparison.Ordinal));
        gate.SetResult();
        await Task.WhenAll(first, waiting);
        Assert.Equal(2, builder.Executed.Count);
    }

    [Fact]
    public async Task RunAsync_XcodeTestOnASimulatorByName_GetsACloneForTheRunAndGivesItBack()
    {
        var (runs, builder) = Of(has: [Capability.Xcode]);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Xcode, ["test", "-scheme", "App", "-destination", "platform=iOS Simulator,name=iPhone 17,OS=27.1"], null, new Steps(), CancellationToken.None);

        var (device, clone) = Assert.Single(builder.Acquired);
        Assert.Equal("iPhone 17", device);
        Assert.StartsWith(XcodeCommand.SimulatorPrefix, clone, StringComparison.Ordinal);
        var command = Assert.Single(builder.Executed).Command;
        Assert.Contains("platform=iOS Simulator,id=UDID-1", command);
        Assert.Contains("-derivedDataPath", command);
        Assert.Equal(["UDID-1"], builder.Released);
        Assert.NotNull(answer.Value);
    }

    [Fact]
    public async Task RunAsync_DockerBuild_CarriesTheOwnerLabel()
    {
        var (runs, builder) = Of(has: [Capability.Docker]);

        await runs.RunAsync("/Users/me/Sample", BuildKind.Docker, ["build", "-t", "sample", "."], null, new Steps(), CancellationToken.None);

        Assert.Equal(["docker", "build", "--label", "snail.owner=buildbridge/Sample/main", "-t", "sample", "."], Assert.Single(builder.Executed).Command);
        Assert.Empty(builder.Acquired);
    }

    [Fact]
    public async Task RunAsync_StaleWorkspaceOfTheRepository_IsSweptAfterTheRun()
    {
        var (runs, builder) = Of();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        builder.Found = new Survey(Mini, [new WorkspaceState(new WorkspaceKey("Sample", "old"), old, 5), new WorkspaceState(new WorkspaceKey("Other", "old"), old, 5)], [], [], 0, 0, 400);

        var answer = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], null, new Steps(), CancellationToken.None);

        var swept = Assert.Single(answer.Value!.Swept);
        Assert.Equal("Sample/old", swept.Item.Name);
        Assert.Equal(["Sample/old"], builder.CarriedOut.Select(item => item.Name));
    }

    [Fact]
    public async Task RunAsync_HostShortOfRoom_SweepsTheCachesFirst()
    {
        var (runs, builder) = Of();
        builder.Up[Mini.Name] = new HostState(Mini, true, [new Toolchain(Capability.Dotnet, "1")], 5, null);
        builder.Found = new Survey(Mini, [], [], [], 3.5, 0, 5);
        var steps = new Steps();

        await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], null, steps, CancellationToken.None);

        Assert.Contains(builder.CarriedOut, item => item.Kind == SweepKind.BuildCache);
        Assert.Contains(steps.Said, step => step.StartsWith("swept mini", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dotnet", BuildKind.Dotnet)]
    [InlineData("Xcode", BuildKind.Xcode)]
    [InlineData("DOCKER", BuildKind.Docker)]
    public void KindOf_AnyCase_IsTheKind(string word, BuildKind expected) => Assert.Equal(expected, Runs.KindOf(word).Value);

    [Fact]
    public void KindOf_UnknownWord_NamesTheKindsThereAre()
    {
        Assert.Contains("dotnet, xcode or docker", Runs.KindOf("make").Error, StringComparison.Ordinal);
    }
}
