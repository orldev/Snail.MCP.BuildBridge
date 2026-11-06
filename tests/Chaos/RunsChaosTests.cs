using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tests.Application;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.BuildBridge.Tests.Chaos;

/// <summary>Runs and deliveries that meet on one workspace, and runs cancelled while they wait.</summary>
public sealed class RunsChaosTests
{
    private static readonly Host Mini = new("mini", "mini.local");

    private sealed class Quiet : IProgress<JobProgress>
    {
        public void Report(JobProgress value)
        {
        }
    }

    private static (Runs Runs, Deliveries Deliveries, FakeBuilder Builder) Of()
    {
        var builder = new FakeBuilder();
        builder.Up[Mini.Name] = new HostState(Mini, true, [new Toolchain(Capability.Dotnet, "1")], 400, null, "/Users/dev");
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        var hosts = new Hosts(options, builder);
        var copies = new FakeCopies();
        var logs = new Logs(TimeProvider.System);
        var turns = new WorkspaceTurns();

        return (new Runs(hosts, copies, builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), turns, new FakeSecrets(), options), new Deliveries(hosts, copies, builder, turns), builder);
    }

    /// <summary>
    /// Breaks the one-run-per-workspace rule from the side. A run holds the workspace while its command runs; build_sync
    /// delivers into that very workspace without asking for the turn, and rsync --delete pulls files from under the
    /// running build. The second delivery must wait until the run ends.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_WhileARunHoldsTheWorkspace_WaitsForTheRun()
    {
        var (runs, deliveries, builder) = Of();
        var gate = new TaskCompletionSource();
        builder.Held = gate.Task;

        var run = runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], [], [], null, new Quiet(), CancellationToken.None);
        await Task.Delay(100);
        var sync = deliveries.DeliverAsync("/Users/me/Sample", null, false, CancellationToken.None);
        await Task.Delay(100);

        int deliveredWhileRunning = builder.Delivered.Count;
        gate.SetResult();
        await Task.WhenAll(run, sync);
        Assert.Equal(1, deliveredWhileRunning);
    }

    /// <summary>A run cancelled while it waits for the workspace lets the next one through: the turn is neither lost nor given back twice.</summary>
    [Fact]
    public async Task RunAsync_CancelledWhileWaitingForTheWorkspace_LetsTheNextRunThrough()
    {
        var (runs, _, builder) = Of();
        var gate = new TaskCompletionSource();
        builder.Held = gate.Task;
        using var cancelled = new CancellationTokenSource();

        var first = runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["build"], [], [], null, new Quiet(), CancellationToken.None);
        await Task.Delay(100);
        var waiting = runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["test"], [], [], null, new Quiet(), cancelled.Token);
        await Task.Delay(100);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        gate.SetResult();
        await first;
        builder.Held = Task.CompletedTask;

        var third = await runs.RunAsync("/Users/me/Sample", BuildKind.Dotnet, ["pack"], [], [], null, new Quiet(), CancellationToken.None);

        Assert.Null(third.Error);
        Assert.Equal(2, builder.Executed.Count);
    }

    /// <summary>Fifty runs on fifty workspaces at once: every one runs, none waits for another.</summary>
    [Fact]
    public async Task RunAsync_FiftyWorkspacesAtOnce_AllRunSideBySide()
    {
        var builder = new FakeBuilder();
        builder.Up[Mini.Name] = new HostState(Mini, true, [new Toolchain(Capability.Dotnet, "1")], 400, null, "/Users/dev");
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] });
        var logs = new Logs(TimeProvider.System);
        var hosts = new Hosts(options, builder);
        var work = Enumerable.Range(0, 50).Select(number =>
        {
            var copies = new FakeCopies { Copy = new LocalCopy($"/Users/me/Sample{number}", $"Sample{number}", "main", null, false) };
            var runs = new Runs(hosts, copies, builder, logs, new Sweeps(builder, logs, options, TimeProvider.System), new WorkspaceTurns(), new FakeSecrets(), options);

            return runs.RunAsync(copies.Copy.Path, BuildKind.Dotnet, ["build"], [], [], null, new Quiet(), CancellationToken.None);
        });

        var answers = await Task.WhenAll(work);

        Assert.All(answers, answer => Assert.Null(answer.Error));
        Assert.Equal(50, builder.Executed.Count);
    }
}
