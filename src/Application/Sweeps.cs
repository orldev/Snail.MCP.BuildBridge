using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>What a host gives back: a sweep planned and shown, carried out only as shown; a sweep after a run; a sweep before one when the host is short of room.</summary>
/// <remarks>
/// Nothing on a host is the only copy of anything, so a sweep erases. A plan is kept ten minutes under its id and
/// carried out only by that id, the way Snail.MCP.CleanUp does it: what is removed is what was looked at.
/// </remarks>
public sealed class Sweeps(IBuilder builder, Logs logs, IOptions<BuildOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, SweepPlan> _plans = new(StringComparer.Ordinal);

    /// <summary>The ages the sweeps go by.</summary>
    public SweepRules Rules => options.Value.ToSweepRules();

    /// <summary>A sweep of <paramref name="scope"/> on <paramref name="host"/>, decided and kept under its id.</summary>
    public async Task<Answer<SweepPlan>> PlanAsync(Host host, SweepScope scope, WorkspaceKey? workspace, string? repository, CancellationToken cancellationToken)
    {
        var surveyed = await builder.SurveyAsync(host, cancellationToken);
        if (!surveyed.TryGet(out var survey))
            return surveyed.As<SweepPlan>();

        var now = clock.GetUtcNow();
        var items = Sweep.Decide(survey, Rules, now, scope, workspace, repository).ToList();
        if (scope is SweepScope.Caches or SweepScope.Everything && OldLogs(now).Count is > 0 and var old)
            items.Add(new SweepItem(SweepKind.Logs, $"{Rules.JobDays}d", null, $"{old} logs older than {Rules.JobDays} days"));
        if (scope is SweepScope.Caches or SweepScope.Everything)
            items.AddRange(await StrandedAsync(cancellationToken));

        var plan = new SweepPlan(Guid.NewGuid().ToString("N")[..10], host, scope, items, now);
        _plans[plan.Id] = plan;
        Forget(now);

        return Answer.Ok(plan);
    }

    /// <summary>Carries out the plan <paramref name="planId"/> names, item by item.</summary>
    public async Task<Answer<IReadOnlyList<SweepResult>>> ApplyAsync(string planId, CancellationToken cancellationToken)
    {
        if (!_plans.TryRemove(planId, out var plan) || !plan.IsCurrent(clock.GetUtcNow()))
            return Answer.Failed(Messages.UnknownPlan(planId), type: Refusals.NoSuchPlan);

        return Answer.Ok(await CarryOutAsync(plan.Host, plan.Items, cancellationToken));
    }

    /// <summary>The sweep after a run of <paramref name="repository"/> on <paramref name="host"/>: its stale workspaces, orphan derived data, old logs; what went is answered.</summary>
    public async Task<IReadOnlyList<SweepResult>> AfterRunAsync(Host host, string repository, CancellationToken cancellationToken)
    {
        var surveyed = await builder.SurveyAsync(host, cancellationToken);
        if (!surveyed.TryGet(out var survey))
            return [];

        var now = clock.GetUtcNow();
        var items = Sweep.AfterRun(survey, Rules, now, repository).ToList();
        if (OldLogs(now).Count is > 0 and var old)
            items.Add(new SweepItem(SweepKind.Logs, $"{Rules.JobDays}d", null, $"{old} logs older than {Rules.JobDays} days"));
        items.AddRange(await StrandedAsync(cancellationToken));

        return await CarryOutAsync(host, items, cancellationToken);
    }

    /// <summary>The sessions whose copy on this machine is gone, as items of a sweep: each holds a connection to its host for nothing.</summary>
    private async Task<IReadOnlyList<SweepItem>> StrandedAsync(CancellationToken cancellationToken)
    {
        var stranded = await builder.StrandedAsync(cancellationToken);

        return [.. stranded.Select(name => new SweepItem(SweepKind.Session, name, null, "its copy on this machine is gone"))];
    }

    /// <summary>The sweep before a run when <paramref name="state"/> shows the host short of room: the caches scope, carried out at once; nothing when there is room.</summary>
    public async Task<IReadOnlyList<SweepResult>> BeforeRunAsync(HostState state, CancellationToken cancellationToken)
    {
        if (state.FreeGigabytes is not { } free || !Rules.IsLow(free))
            return [];

        var planned = await PlanAsync(state.Host, SweepScope.Caches, null, null, cancellationToken);
        if (!planned.TryGet(out var plan))
            return [];

        _plans.TryRemove(plan.Id, out _);

        return await CarryOutAsync(plan.Host, plan.Items, cancellationToken);
    }

    /// <summary>The scope a tool was given, or why the word is not one.</summary>
    public static Answer<SweepScope> ScopeOf(string word) =>
        Enum.TryParse<SweepScope>(word, true, out var scope) && Enum.IsDefined(scope)
            ? Answer.Ok(scope)
            : Answer.Failed(Messages.UnknownScope(word), type: Refusals.Arguments);

    private async Task<IReadOnlyList<SweepResult>> CarryOutAsync(Host host, IReadOnlyList<SweepItem> items, CancellationToken cancellationToken)
    {
        var results = new List<SweepResult>();
        foreach (var item in items)
        {
            if (item.Kind == SweepKind.Logs)
            {
                results.Add(new SweepResult(item, true, $"{EraseOldLogs(clock.GetUtcNow())} erased"));
                continue;
            }

            var done = await builder.CarryOutAsync(host, item, cancellationToken);
            results.Add(new SweepResult(item, done.Value, done.Error));
        }

        return results;
    }

    private IReadOnlyList<string> OldLogs(DateTimeOffset now)
    {
        if (!Directory.Exists(logs.Folder))
            return [];

        var age = TimeSpan.FromDays(Rules.JobDays);

        return [.. Directory.EnumerateFiles(logs.Folder, "*.log").Where(file => now - File.GetLastWriteTimeUtc(file) > age)];
    }

    private int EraseOldLogs(DateTimeOffset now)
    {
        int erased = 0;
        foreach (var file in OldLogs(now))
        {
            try
            {
                File.Delete(file);
                erased++;
            }
            catch (Exception kept) when (kept is IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return erased;
    }

    private void Forget(DateTimeOffset now)
    {
        foreach (var stale in _plans.Where(pair => !pair.Value.IsCurrent(now)).ToList())
            _plans.TryRemove(stale);
    }
}
