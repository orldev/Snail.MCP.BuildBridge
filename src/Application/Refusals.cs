using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>The <c>type</c> of every failure this server answers: one word per kind, which a program branches on instead of the words of <c>error</c>.</summary>
/// <remarks>
/// The SDK's words where one fits — <c>arguments</c>, <c>outside</c>, <c>absent</c>, <c>timeout</c> — and the server's own
/// where none does. A job that could not happen ends failed with the type of what stopped it; before these, every one
/// answered <c>failed</c>, and a program could not tell a host that did not answer from a copy that was no copy.
/// </remarks>
public static class Refusals
{
    /// <summary>A word the call gave that this server cannot take: a kind, a scope, a package, a variable, a fetch of nothing, a script not named, a sweep with nothing to sweep.</summary>
    /// <remarks>The SDK's word for arguments that do not fit, which its own check answers before a tool runs.</remarks>
    public const string Arguments = ToolFailures.Arguments;

    /// <summary>A path that leads out of where it must stay: a log out of the jobs folder, a script or a fetched path out of the workspace.</summary>
    public const string Outside = Reached.Outside;

    /// <summary>A call that named no path where one must be: build_logs with a blank log.</summary>
    public const string NoPath = Reached.NoPath;

    /// <summary>A file that is not there where one must be: a log, a script, the gradle wrapper.</summary>
    public const string Absent = Reached.Absent;

    /// <summary>Work on a host that ran past its time and was stopped.</summary>
    /// <remarks>The word Snail.Sdk.Mcp.Programs gives a program that ran past its time.</remarks>
    public const string Timeout = "timeout";

    /// <summary>No host is configured.</summary>
    public const string NoHosts = "noHosts";

    /// <summary>A host the settings do not name.</summary>
    public const string NoSuchHost = "noSuchHost";

    /// <summary>A job this server does not know.</summary>
    public const string NoSuchJob = "noSuchJob";

    /// <summary>A sweep plan this server does not hold, never shown or shown too long ago.</summary>
    public const string NoSuchPlan = "noSuchPlan";

    /// <summary>A host that did not answer over ssh.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>A host without the toolchain the build needs: Xcode, Docker, the Android SDK, dotnet.</summary>
    public const string NoToolchain = "noToolchain";

    /// <summary>A path outside any git working copy.</summary>
    public const string NotACopy = "notACopy";

    /// <summary>A program this machine lacks: git, rsync, ssh, mutagen.</summary>
    public const string NotInstalled = "notInstalled";

    /// <summary>A secret the keychain of this machine did not give.</summary>
    public const string NoSecret = "noSecret";

    /// <summary>A host that did not say where its home is, so no container can be given the workspace.</summary>
    public const string NoHome = "noHome";

    /// <summary>A copy that did not reach the host, by rsync or by a Mutagen session.</summary>
    public const string NotDelivered = "notDelivered";

    /// <summary>Paths that were not brought back from the host.</summary>
    public const string NotCollected = "notCollected";

    /// <summary>A provisioning that broke off before it said what it saw to.</summary>
    public const string NotProvisioned = "notProvisioned";

    /// <summary>Packages, an SDK or workloads the host could not put under its root.</summary>
    public const string NotSupplied = "notSupplied";

    /// <summary>A simulator that could not be cloned for the run.</summary>
    public const string NoSimulator = "noSimulator";

    /// <summary>A project xcodegen did not generate from its spec.</summary>
    public const string NotGenerated = "notGenerated";

    /// <summary>A host that could not be surveyed for what a sweep would take.</summary>
    public const string NotSurveyed = "notSurveyed";
}
