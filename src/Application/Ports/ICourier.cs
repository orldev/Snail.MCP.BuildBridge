using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Application.Ports;

/// <summary>What carries a working copy of this machine into a host's workspace.</summary>
/// <remarks>
/// A builder owns the workspace — its marker, its runs, its removal — and a courier only fills it: rsync mirrors the
/// copy before a run, a Mutagen session keeps it mirrored as the copy changes. The builder asks the courier before a
/// run and tells it when the workspace goes, so a session does not outlive what it mirrored into.
/// </remarks>
public interface ICourier
{
    /// <summary>Delivers a copy into its workspace on a host, or with a dry run lists what a delivery would send; the answer says what moved.</summary>
    Task<Answer<Delivery>> DeliverAsync(Host host, LocalCopy copy, bool isDryRun, CancellationToken cancellationToken);

    /// <summary>Lets go of whatever the courier keeps for a workspace — a session — because the workspace is being removed.</summary>
    Task ForgetAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken);

    /// <summary>What the courier still keeps for copies that are gone from this machine — sessions of a worktree removed, a scratch folder erased — by name.</summary>
    Task<IReadOnlyList<string>> StrandedAsync(CancellationToken cancellationToken);

    /// <summary>Lets what the courier keeps for the workspaces of <paramref name="keys"/> on a host rest — a session paused, holding no connection until the next delivery — and answers the names of those it put to rest.</summary>
    Task<IReadOnlyList<string>> RestAsync(Host host, IReadOnlyList<WorkspaceKey> keys, CancellationToken cancellationToken);

    /// <summary>Lets everything the courier keeps connected rest, whatever the workspace, and answers the names: what is done when a host has no connection left to give.</summary>
    Task<IReadOnlyList<string>> RestAsync(CancellationToken cancellationToken);

    /// <summary>Lets go of one thing <see cref="StrandedAsync"/> named; a name that is not the courier's own is left alone.</summary>
    Task EndAsync(string name, CancellationToken cancellationToken);
}
