using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Jobs;

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
            return ToolResponse.Failure(Messages.NoHosts, Messages.NoHostsHint);

        var states = await hosts.ProbeAllAsync(cancellationToken);

        return ToolResponse.Success(new JsonObject { ["hosts"] = new JsonArray([.. states.Select(ToolResponse.Data)]) });
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
        if (named.Value is null)
            return ToolResponse.Failure(named.Error!, named.Hint);

        var job = jobs.Start(ProvisionKind, async (_, token) => Outcome(await provisioning.RunAsync(host, token)), AnswerJson.Default.JsonObject);
        var report = await job.ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);

        return ToolResponse.Job(report);
    }

    /// <summary>Delivers a working copy to a host, or lists what a delivery would send.</summary>
    [McpServerTool(Name = "build_sync", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Sync)]
    public async Task<CallToolResult> SyncAsync(
        [Description(ToolDescriptions.SyncPath)] string path,
        [Description(ToolDescriptions.HostName)] string? host = null,
        [Description(ToolDescriptions.SyncDryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default) =>
        ToolResponse.From(await deliveries.DeliverAsync(path, host, dryRun, cancellationToken), ToolResponse.Data);

    /// <summary>Builds a working copy on a host as a job.</summary>
    [McpServerTool(Name = "build_run", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description(ToolDescriptions.Run)]
    public async Task<CallToolResult> RunAsync(
        [Description(ToolDescriptions.RunPath)] string path,
        [Description(ToolDescriptions.RunKind)] string kind,
        [Description(ToolDescriptions.RunArguments)] string[] arguments,
        [Description(ToolDescriptions.HostName)] string? host = null,
        [Description(ToolDescriptions.Wait)] int wait = 60,
        CancellationToken cancellationToken = default)
    {
        var wanted = Runs.KindOf(kind);
        if (wanted.Value is not { } known)
            return ToolResponse.Failure(wanted.Error!, wanted.Hint);

        var named = hosts.Named(host);
        if (named.Value is null)
            return ToolResponse.Failure(named.Error!, named.Hint);

        var job = jobs.Start(Runs.ToolWord(known), async (progress, token) => Outcome(await runs.RunAsync(path, known, arguments, host, progress, token)), AnswerJson.Default.JsonObject);
        var report = await job.ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);

        return ToolResponse.Job(report);
    }

    /// <summary>Reads the end of a build's log.</summary>
    [McpServerTool(Name = "build_logs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Logs)]
    public async Task<CallToolResult> LogsAsync(
        [Description(ToolDescriptions.LogsLog)] string log,
        [Description(ToolDescriptions.LogsLines)] int lines = LogLines,
        CancellationToken cancellationToken = default)
    {
        string full = Path.GetFullPath(log);
        if (!full.StartsWith(logs.Folder + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
            return ToolResponse.Failure(Messages.NotALog(log, logs.Folder), null);

        var tail = await Logs.TailAsync(full, Math.Clamp(lines, 1, MaxLogLines), cancellationToken);

        return ToolResponse.Success(new JsonObject { ["log"] = full, ["lines"] = tail.Count, ["text"] = string.Join('\n', tail) });
    }

    /// <summary>Stops a running job.</summary>
    [McpServerTool(Name = "build_cancel", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Cancel)]
    public CallToolResult Cancel([Description(ToolDescriptions.CancelJob)] string job)
    {
        var found = jobs.Find(job);
        if (found is null)
            return ToolResponse.Failure(Messages.UnknownJob(job), null);

        bool wasRunning = found.Report.IsRunning;
        found.Cancel();

        return ToolResponse.Success(new JsonObject { ["job"] = found.Uri, ["cancelled"] = wasRunning });
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
        ToolResponse.From(await collections.FetchAsync(path, host, paths, into, cancellationToken), ToolResponse.Data);

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
            return ToolResponse.From(await sweeps.ApplyAsync(planId, cancellationToken), ToolResponse.Data);

        var wanted = Sweeps.ScopeOf(scope);
        if (wanted.Value is not { } known)
            return ToolResponse.Failure(wanted.Error!, wanted.Hint);

        var ready = await hosts.ReadyAsync(host, null, cancellationToken);
        if (ready.Value is not { } state)
            return ToolResponse.Failure(ready.Error!, ready.Hint);

        WorkspaceKey? workspace = null;
        if (path is not null)
        {
            var copy = await copies.ReadAsync(path, cancellationToken);
            if (copy.Value is not { } found)
                return ToolResponse.Failure(copy.Error!, copy.Hint);

            workspace = found.Workspace;
            repository ??= found.Repository;
        }
        if (known is SweepScope.Workspace && workspace is null || known is SweepScope.Repository && repository is null)
            return ToolResponse.Failure(Messages.ScopeNeedsCopy, null);

        return ToolResponse.From(await sweeps.PlanAsync(state.Host, known, workspace, repository, cancellationToken), ToolResponse.Data);
    }

    /// <summary>A job's result: the data, or the error it ended with as data of its own.</summary>
    private static JsonObject Outcome<T>(Answer<T> answer, Func<T, JsonObject> data) =>
        answer.Value is { } value ? data(value) : new JsonObject { ["ok"] = false, ["error"] = answer.Error, ["hint"] = answer.Hint };

    private static JsonObject Outcome(Answer<Provisioned> answer) => Outcome(answer, ToolResponse.Data);

    private static JsonObject Outcome(Answer<BuildReport> answer) => Outcome(answer, ToolResponse.Data);
}
