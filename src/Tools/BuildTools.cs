using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.MCP.BuildBridge.Tools;

/// <summary>The hosts and what reaches them: provisioning, a delivery of a working copy, a build, its log, and what comes back.</summary>
[McpServerToolType]
public sealed class BuildTools(Hosts hosts, Deliveries deliveries, Provisioning provisioning, Runs runs, Collections collections, Sweeps sweeps, ICopies copies, Logs logs, Jobs jobs)
{
    /// <summary>The kind of job provisioning is, the second part of its resource's address.</summary>
    public const string ProvisionKind = "provision";

    /// <summary>Lines build_logs reads unless asked for more.</summary>
    public const int LogLines = 100;

    /// <summary>Most lines build_logs reads.</summary>
    public const int MaxLogLines = 2000;

    /// <summary>Lists the hosts as they stand now.</summary>
    [McpServerTool(Name = "build_hosts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Hosts)]
    public async Task<CallToolResult> HostsAsync(CancellationToken cancellationToken = default)
    {
        if (hosts.All.Count == 0)
            return ToolAnswer.Failed(Messages.NoHosts, Messages.NoHostsHint, Refusals.NoHosts);

        var states = await hosts.ProbeAllAsync(cancellationToken);

        return ToolAnswer.Ok(new JsonObject { ["hosts"] = new JsonArray([.. states.Select(AnswerFields.Of)]) });
    }

    /// <summary>Provisions a host as a job.</summary>
    [McpServerTool(Name = "build_provision", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Provision)]
    public async Task<CallToolResult> ProvisionAsync(
        [Description(ToolDescriptions.HostName)] string? host = null,
        [Description(ToolDescriptions.Wait)] int wait = 60,
        CancellationToken cancellationToken = default)
    {
        var named = hosts.Named(host);
        if (!named.IsSuccess)
            return ToolAnswer.Failed(named);

        var job = jobs.Start(ProvisionKind, async (_, token) => (await provisioning.RunAsync(host, token)).Map(AnswerFields.Of), ToolAnswer.ObjectInfo);
        var report = await job.ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);

        return JobAnswer.Envelope(report);
    }

    /// <summary>Delivers a working copy to a host, or lists what a delivery would send.</summary>
    [McpServerTool(Name = "build_sync", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Sync)]
    public async Task<CallToolResult> SyncAsync(
        [Description(ToolDescriptions.SyncPath)] string path,
        [Description(ToolDescriptions.HostName)] string? host = null,
        [Description(ToolDescriptions.SyncDryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await deliveries.DeliverAsync(path, host, dryRun, cancellationToken), AnswerFields.Of);

    /// <summary>Builds a working copy on a host as a job.</summary>
    [McpServerTool(Name = "build_run", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description(ToolDescriptions.Run)]
    public async Task<CallToolResult> RunAsync(
        [Description(ToolDescriptions.RunPath)] string path,
        [Description(ToolDescriptions.RunKind)] string kind,
        [Description(ToolDescriptions.RunArguments)] string[] arguments,
        [Description(ToolDescriptions.RunNeeds)] string[]? needs = null,
        [Description(ToolDescriptions.RunEnvironment)] string[]? environment = null,
        [Description(ToolDescriptions.HostName)] string? host = null,
        [Description(ToolDescriptions.Wait)] int wait = 60,
        CancellationToken cancellationToken = default)
    {
        var wanted = Runs.KindOf(kind);
        if (!wanted.TryGet(out var known))
            return ToolAnswer.Failed(wanted);

        var named = hosts.Named(host);
        if (!named.IsSuccess)
            return ToolAnswer.Failed(named);

        var job = jobs.Start(Runs.ToolWord(known), async (progress, token) => (await runs.RunAsync(path, known, arguments, needs ?? [], environment ?? [], host, progress, token)).Map(AnswerFields.Of), ToolAnswer.ObjectInfo);
        var report = await job.ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);

        return JobAnswer.Envelope(report);
    }

    /// <summary>Reads the end of a build's log.</summary>
    /// <remarks>
    /// The log and the jobs folder are compared with every link on them followed: by their names alone, a link in the
    /// folder that led out of it passed, and its target was read. Both sides, because the folder itself may lie under a
    /// link — /var is /private/var on macOS — and a log in it would otherwise be refused.
    /// </remarks>
    [McpServerTool(Name = "build_logs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Logs)]
    public async Task<CallToolResult> LogsAsync(
        [Description(ToolDescriptions.LogsLog)] string log,
        [Description(ToolDescriptions.LogsLines)] int lines = LogLines,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(log))
            return ToolAnswer.Failed(Messages.NotALog(log, logs.Folder), type: Refusals.NoPath);

        string full = Path.GetFullPath(log);
        string? real = RealPath.Of(full);
        string folder = RealPath.Of(logs.Folder) ?? logs.Folder;
        if (real is null || !real.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return ToolAnswer.Failed(Messages.NotALog(log, logs.Folder), type: Refusals.Outside);
        if (!File.Exists(real))
            return ToolAnswer.Failed(Messages.NoLog(log), type: Refusals.Absent);

        var tail = await Logs.TailAsync(real, Math.Clamp(lines, 1, MaxLogLines), cancellationToken);

        return ToolAnswer.Ok(new JsonObject { ["log"] = full, ["lines"] = tail.Count, ["text"] = string.Join('\n', tail) });
    }

    /// <summary>Stops a running job.</summary>
    [McpServerTool(Name = "build_cancel", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Cancel)]
    public CallToolResult Cancel([Description(ToolDescriptions.CancelJob)] string job)
    {
        var found = jobs.Find(job);
        if (found is null)
            return ToolAnswer.Failed(Messages.UnknownJob(job), type: Refusals.NoSuchJob);

        bool wasRunning = found.Report.IsRunning;
        found.Cancel();

        return ToolAnswer.Ok(new JsonObject { ["job"] = found.Uri, ["cancelled"] = wasRunning });
    }

    /// <summary>Brings paths of a workspace back.</summary>
    [McpServerTool(Name = "build_fetch", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Fetch)]
    public async Task<CallToolResult> FetchAsync(
        [Description(ToolDescriptions.FetchPath)] string path,
        [Description(ToolDescriptions.FetchPaths)] string[] paths,
        [Description(ToolDescriptions.HostName)] string? host = null,
        [Description(ToolDescriptions.FetchInto)] string? into = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await collections.FetchAsync(path, host, paths, into, cancellationToken), AnswerFields.Of);

    /// <summary>Plans a sweep, or carries a plan out.</summary>
    [McpServerTool(Name = "build_clean", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Clean)]
    public async Task<CallToolResult> CleanAsync(
        [Description(ToolDescriptions.CleanScope)] string scope,
        [Description(ToolDescriptions.CleanPath)] string? path = null,
        [Description(ToolDescriptions.CleanRepository)] string? repository = null,
        [Description(ToolDescriptions.HostName)] string? host = null,
        [Description(ToolDescriptions.CleanPlan)] string? planId = null,
        CancellationToken cancellationToken = default)
    {
        if (planId is not null)
            return ToolAnswer.From(await sweeps.ApplyAsync(planId, cancellationToken), AnswerFields.Of);

        var wanted = Sweeps.ScopeOf(scope);
        if (!wanted.TryGet(out var known))
            return ToolAnswer.Failed(wanted);

        var ready = await hosts.ReadyAsync(host, [], cancellationToken);
        if (!ready.TryGet(out var state))
            return ToolAnswer.Failed(ready);

        WorkspaceKey? workspace = null;
        if (path is not null)
        {
            var copy = await copies.ReadAsync(path, cancellationToken);
            if (!copy.TryGet(out var found))
                return ToolAnswer.Failed(copy);

            workspace = found.Workspace;
            repository ??= found.Repository;
        }
        if (known is SweepScope.Workspace && workspace is null || known is SweepScope.Repository && repository is null)
            return ToolAnswer.Failed(Messages.ScopeNeedsCopy, type: Refusals.Arguments);

        return ToolAnswer.From(await sweeps.PlanAsync(state.Host, known, workspace, repository, cancellationToken), AnswerFields.Of);
    }
}
