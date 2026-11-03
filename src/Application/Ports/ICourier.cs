using Snail.MCP.BuildBridge.Domain;

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
}
