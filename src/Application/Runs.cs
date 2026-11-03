using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>A build of a working copy on a host: the copy delivered, the feeds mirrored, the command run in the workspace, the log read for what the tests said.</summary>
/// <remarks>
/// One workspace takes one run at a time: two sessions on one branch would otherwise write into one <c>obj</c>, and a
/// delivery under a running build would pull files from under it. A run that finds the workspace busy waits its turn,
/// and says so in its progress.
/// </remarks>
public sealed class Runs(Hosts hosts, ICopies copies, IBuilder builder, Logs logs, Sweeps sweeps, IOptions<BuildOptions> options)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaces = new(StringComparer.Ordinal);

    /// <summary>Runs <paramref name="kind"/>'s program with <paramref name="arguments"/> in the workspace of the copy <paramref name="path"/> lies in, on the host named or the first.</summary>
    public async Task<Answer<BuildReport>> RunAsync(string path, BuildKind kind, IReadOnlyList<string> arguments, string? host, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        var copy = await copies.ReadAsync(path, cancellationToken);
        if (copy.Value is not { } found)
            return Answer.Failed<BuildReport>(copy.Error!, copy.Hint);

        var ready = await hosts.ReadyAsync(host, kind.Needs(), cancellationToken);
        if (ready.Value is not { } state)
            return Answer.Failed<BuildReport>(ready.Error!, ready.Hint);

        var turn = _workspaces.GetOrAdd($"{state.Host.Name}/{found.Workspace.Folder}", _ => new SemaphoreSlim(1, 1));
        if (!await turn.WaitAsync(0, cancellationToken))
        {
            progress.Report(new JobProgress(0, 1, $"waiting for {found.Workspace.Folder} on {state.Host.Name}, where another run is going"));
            await turn.WaitAsync(cancellationToken);
        }

        try
        {
            var swept = await sweeps.BeforeRunAsync(state, cancellationToken);
            if (swept.Count > 0)
                progress.Report(new JobProgress(0, 5, $"swept {state.Host.Name}, which was short of room"));

            var ran = await RanAsync(state.Host, found, kind, arguments, progress, cancellationToken);
            if (ran.Value is not { } report)
                return ran;

            var after = await sweeps.AfterRunAsync(state.Host, found.Repository, cancellationToken);

            return Answer.Ok(report with { Swept = [.. swept, .. after] });
        }
        finally
        {
            turn.Release();
        }
    }

    private async Task<Answer<BuildReport>> RanAsync(Host host, LocalCopy copy, BuildKind kind, IReadOnlyList<string> arguments, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new JobProgress(1, 5, $"delivering {copy.Repository} ({copy.Branch}) to {host.Name}"));
        var delivered = await builder.DeliverAsync(host, copy, false, cancellationToken);
        if (delivered.Value is not { } delivery)
            return Answer.Failed<BuildReport>(delivered.Error!, delivered.Hint);

        progress.Report(new JobProgress(2, 5, "mirroring the feeds"));
        foreach (var feed in options.Value.Feeds)
        {
            var mirrored = await builder.MirrorAsync(host, feed.Path, $"feeds/{feed.Name}", cancellationToken);
            if (mirrored.Value is null)
                return Answer.Failed<BuildReport>(mirrored.Error!, mirrored.Hint);
        }

        var shaped = Shaped(kind, arguments, copy.Workspace);
        string? simulator = null;
        if (kind == BuildKind.Xcode && XcodeCommand.SimulatorNamed(shaped) is { } device)
        {
            progress.Report(new JobProgress(3, 5, $"cloning the simulator {device}"));
            var acquired = await builder.AcquireSimulatorAsync(host, device, $"{XcodeCommand.SimulatorPrefix}{Path.GetRandomFileName()[..6]}", cancellationToken);
            if (acquired.Value is not { } udid)
                return Answer.Failed<BuildReport>(acquired.Error!, acquired.Hint);

            simulator = udid;
            shaped = XcodeCommand.OnDevice(shaped, udid);
        }

        try
        {
            string log = logs.New(ToolWord(kind));
            List<string> command = [kind.Program(), .. shaped];
            progress.Report(new JobProgress(4, 5, $"running {string.Join(' ', command)}; log {log}"));

            var executed = await builder.ExecuteAsync(host, copy.Workspace, command, log, TimeSpan.FromSeconds(options.Value.RunSeconds), cancellationToken);
            if (executed.Value is not { } execution)
                return Answer.Failed<BuildReport>(executed.Error!, executed.Hint);

            var tests = TestSummary.Parse(await File.ReadAllTextAsync(log, cancellationToken));

            return Answer.Ok(new BuildReport(host, kind, command, delivery, execution, tests, []));
        }
        finally
        {
            if (simulator is not null)
                await builder.ReleaseSimulatorAsync(host, simulator, CancellationToken.None);
        }
    }

    /// <summary>The arguments as the kind shapes them for the workspace: derived data under the root for xcodebuild, the owner label and the project for docker.</summary>
    public static IReadOnlyList<string> Shaped(BuildKind kind, IReadOnlyList<string> arguments, WorkspaceKey key) => kind switch
    {
        BuildKind.Xcode => XcodeCommand.Shaped(arguments, key),
        BuildKind.Docker => DockerCommand.Shaped(arguments, key),
        _ => arguments
    };

    /// <summary>A kind as the tools spell it.</summary>
    public static string ToolWord(BuildKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The kind a tool was given, or why the word is not one.</summary>
    public static Answer<BuildKind?> KindOf(string word) =>
        Enum.TryParse<BuildKind>(word, true, out var kind) && Enum.IsDefined(kind)
            ? Answer.Ok<BuildKind?>(kind)
            : Answer.Failed<BuildKind?>(Messages.UnknownKind(word));
}
