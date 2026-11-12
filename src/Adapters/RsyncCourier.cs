using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>A copy mirrored into its workspace with rsync, once per delivery.</summary>
/// <remarks>
/// What git ignores and what a build makes again are excluded, and an excluded folder already on the host — <c>bin</c>,
/// <c>obj</c> — is left alone, so a build there stays incremental. A tracked file deleted here is deleted there. The
/// excludes go to rsync on its standard input: openrsync knows no filter rules, and the list can be long.
/// </remarks>
public sealed class RsyncCourier(IPrograms programs, ICopies copies, RemoteShell shell, IOptions<BuildOptions> options, TimeProvider clock) : ICourier
{
    /// <inheritdoc/>
    public async Task<Answer<Delivery>> DeliverAsync(Host host, LocalCopy copy, bool isDryRun, CancellationToken cancellationToken)
    {
        var key = copy.Workspace;
        var words = shell.Command(host, RemoteShell.Prelude(host) + $"mkdir -p {RemoteShell.Under(host, key.CopyFolder)}");
        var made = await programs.RunAsync(words[0], words[1..], null, TimeSpan.FromSeconds(options.Value.ProbeSeconds), cancellationToken);
        if (!made.IsSuccess)
            return Answer.Failed(Messages.DeliveryFailed(host.Name, Rsync.Reason(made)), type: Refusals.NotDelivered);

        var ignoredHere = await copies.IgnoredAsync(copy, cancellationToken);
        var ignoredThere = await IgnoredOnHostAsync(host, key, cancellationToken);
        string excludes = string.Join('\n', Excludes(copy, [.. ignoredHere, .. ignoredThere]).Distinct(StringComparer.Ordinal)) + '\n';
        var started = clock.GetTimestamp();

        var arguments = Rsync.Arguments(shell.SshLine, host, isDryRun, true, $"{copy.Path}/", $"{RemoteShell.Rsync(host, key.CopyFolder)}/");
        var run = await programs.RunAsync(options.Value.Rsync, arguments, null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken, excludes);
        if (run.IsMissing)
            return Answer.Failed(Messages.ProgramMissing(options.Value.Rsync), type: Refusals.NotInstalled);
        if (!run.IsSuccess)
            return Answer.Failed(Messages.DeliveryFailed(host.Name, Rsync.Reason(run)), type: Refusals.NotDelivered);

        return Answer.Ok(new Delivery(key, Rsync.Changed(run.Output), Rsync.TransferredBytes(run.Output), clock.GetElapsedTime(started), isDryRun));
    }

    /// <inheritdoc/>
    /// <remarks>rsync keeps nothing between deliveries, so there is nothing to let go of.</remarks>
    public Task ForgetAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> StrandedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc/>
    public Task EndAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> RestAsync(Host host, IReadOnlyList<WorkspaceKey> keys, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> RestAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>What git ignores in the workspace on the host: what the host built for itself under an ignored path, which exists there and not here, and which --delete would otherwise take.</summary>
    /// <remarks>Proven by a chaos test on 2026-11-03: a folder the host made under an ignored path absent on this machine was gone after the next delivery.</remarks>
    private async Task<IReadOnlyList<string>> IgnoredOnHostAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken)
    {
        string script = $"cd {RemoteShell.Under(host, key.CopyFolder)} 2>/dev/null && git ls-files --others --ignored --exclude-standard --directory -z 2>/dev/null";
        var words = shell.Command(host, RemoteShell.Prelude(host) + script);
        var run = await programs.RunAsync(words[0], words[1..], null, TimeSpan.FromSeconds(options.Value.ProbeSeconds), cancellationToken);
        if (!run.IsSuccess)
            return [];

        return run.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>What a delivery leaves out, one pattern each: what a build makes again, a worktree's dead .git file, and what git ignores, anchored at the copy's root.</summary>
    public IReadOnlyList<string> Excludes(LocalCopy copy, IReadOnlyList<string> ignored)
    {
        var patterns = new List<string>(options.Value.Excludes);
        if (copy.IsWorktree)
            patterns.Add("/.git");
        patterns.AddRange(ignored.Select(entry => $"/{entry}"));

        return patterns;
    }
}
