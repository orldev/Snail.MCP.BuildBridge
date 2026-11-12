using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Application.Ports;

/// <summary>A host as this server works it: a copy is delivered, a command runs in it, its outputs come back, and the workspace is removed.</summary>
/// <remarks>
/// The seam the second tier goes through: today a host is reached by ssh and rsync, later a Nomad client on each
/// machine takes the same four steps as a job, and no tool changes.
/// </remarks>
public interface IBuilder
{
    /// <summary>Asks the host what it is: whether it answers, which toolchains it has, how much room is left.</summary>
    Task<HostState> ProbeAsync(Host host, CancellationToken cancellationToken);

    /// <summary>Puts what the manifest names in place under the host's root, everything written going to <paramref name="log"/> on this machine; what has to be done by hand is named, not done.</summary>
    Task<Answer<Provisioned>> ProvisionAsync(Host host, BuilderManifest manifest, string log, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Delivers a copy to its workspace on the host, sending only what changed; with <paramref name="isDryRun"/> it lists what would be sent and sends nothing.</summary>
    Task<Answer<Delivery>> DeliverAsync(Host host, LocalCopy copy, bool isDryRun, CancellationToken cancellationToken);

    /// <summary>Mirrors a folder of this machine to <paramref name="relative"/> under the host's root, sending only what changed.</summary>
    Task<Answer<Delivery>> MirrorAsync(Host host, string source, string relative, CancellationToken cancellationToken);

    /// <summary>Runs a command in a workspace on the host with <paramref name="environment"/> in its environment, its output going to <paramref name="log"/> on this machine as it is written, until it ends or <paramref name="timeout"/> passes.</summary>
    Task<Answer<Execution>> ExecuteAsync(Host host, WorkspaceKey key, IReadOnlyList<string> command, IReadOnlyDictionary<string, string> environment, string log, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Clones the simulator named <paramref name="device"/> on the host into one called <paramref name="clone"/>, for one run, and answers its id.</summary>
    Task<Answer<string>> AcquireSimulatorAsync(Host host, string device, string clone, CancellationToken cancellationToken);

    /// <summary>Shuts down and deletes a simulator a run acquired.</summary>
    Task ReleaseSimulatorAsync(Host host, string udid, CancellationToken cancellationToken);

    /// <summary>Brings the named paths of a workspace, relative to its root, into <paramref name="folder"/> on this machine.</summary>
    Task<Answer<Collected>> CollectAsync(Host host, WorkspaceKey key, IReadOnlyList<string> paths, string folder, CancellationToken cancellationToken);

    /// <summary>Puts a supply under the host's root — Android SDK packages, a .NET SDK, workloads — those not there yet; answers what it put in place, in the supply's words, or why it could not — a licence only a person accepts among the reasons.</summary>
    Task<Answer<IReadOnlyList<string>>> SupplyAsync(Host host, Supply supply, CancellationToken cancellationToken);

    /// <summary>The sessions this server keeps to its hosts for copies that are gone from this machine, by name: each holds a connection for nothing.</summary>
    Task<IReadOnlyList<string>> StrandedAsync(CancellationToken cancellationToken);

    /// <summary>Puts to rest the sessions this server keeps to the host for the workspaces of <paramref name="keys"/>, so they hold no connection until their next delivery; answers the names of those it paused.</summary>
    Task<IReadOnlyList<string>> RestAsync(Host host, IReadOnlyList<WorkspaceKey> keys, CancellationToken cancellationToken);

    /// <summary>What lies under the host's root: every workspace of this server's, the derived data, the images and build cache, the packages, the room left; with <paramref name="isSized"/> false no size is measured, each is 0, and the survey takes a second where it took eight.</summary>
    Task<Answer<Survey>> SurveyAsync(Host host, bool isSized, CancellationToken cancellationToken);

    /// <summary>Removes one item of a sweep from the host; a workspace only when it carries this server's marker.</summary>
    Task<Answer<bool>> CarryOutAsync(Host host, SweepItem item, CancellationToken cancellationToken);

    /// <summary>Removes a workspace from the host, with everything built in it.</summary>
    Task<Answer<bool>> RemoveAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken);
}
