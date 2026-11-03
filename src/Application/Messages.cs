using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>What a caller reads when something did not happen, with what to do about it.</summary>
public static class Messages
{
    /// <summary>No host is configured.</summary>
    public const string NoHosts = "No build host is configured, so there is nowhere to build.";

    /// <summary>Where hosts come from.</summary>
    public const string NoHostsHint = "Name a host in the settings file (~/.config/s3l-mcp-buildbridge/config.json) or as SNAIL_MCP_BUILDBRIDGE_HOSTS__0__NAME and __ADDRESS.";

    /// <summary>A host the settings do not name.</summary>
    public static string UnknownHost(string named, IEnumerable<string> known) =>
        $"No host is called {named}; the hosts are {string.Join(", ", known)}.";

    /// <summary>A host that did not answer over ssh.</summary>
    public static string HostDown(string name, string address, string reason) => $"{name} ({address}) did not answer over ssh: {reason}";

    /// <summary>What makes a host answer.</summary>
    public const string HostDownHint = "Remote Login must be on, and this machine's key in the host's authorized_keys; ssh <address> from a terminal shows what is wrong.";

    /// <summary>A host without the toolchain a kind needs.</summary>
    public static string HostCannot(string name, string capability) => $"{name} has no {capability}, so it cannot take this build.";

    /// <summary>A path outside any git working copy.</summary>
    public static string NotACopy(string path) => $"{path} is not inside a git working copy, so there is nothing to deliver.";

    /// <summary>A program this server needs and did not find.</summary>
    public static string ProgramMissing(string program) => $"{program} is not installed on this machine.";

    /// <summary>A delivery that did not end well.</summary>
    public static string DeliveryFailed(string name, string reason) => $"The copy was not delivered to {name}: {reason}";

    /// <summary>A collection that did not end well.</summary>
    public static string CollectionFailed(string name, string reason) => $"The paths were not brought back from {name}: {reason}";

    /// <summary>Provisioning that did not end well.</summary>
    public static string ProvisionFailed(string name, string reason) => $"{name} was not provisioned: {reason}";

    /// <summary>What a person does when Xcode is missing.</summary>
    public const string InstallXcode = "Install Xcode from the App Store, open it once, and accept its licence: sudo xcodebuild -license accept.";

    /// <summary>What a person does when Docker is missing.</summary>
    public const string InstallDocker = "Install OrbStack (orbstack.dev) and start it; docker then answers over ssh too.";

    /// <summary>What a person does when an Apple Silicon host cannot run x64 programs, which build tools still ship as — protoc of Grpc.Tools among them.</summary>
    public const string InstallRosetta = "Install Rosetta on the host, which means agreeing to Apple's licence: softwareupdate --install-rosetta --agree-to-license; x64 build tools such as protoc need it.";

    /// <summary>What a person does when the host may sleep.</summary>
    public const string KeepAwake = "Keep the host awake: sudo pmset -a disablesleep 1, or Snail SleepOf; a sleeping host takes no builds.";

    /// <summary>A word that is not a kind of build.</summary>
    public static string UnknownKind(string word) => $"\"{word}\" is not a kind of build: dotnet, xcode, docker, linux, android or script.";

    /// <summary>A script run without a script named.</summary>
    public const string NoScriptNamed = "A script run names the script first: its path from the working copy's root, then its arguments.";

    /// <summary>A script the copy does not have.</summary>
    public static string NoScript(string path, string copy) => $"{path} is not a file of the working copy at {copy}, and would not be on the host either.";

    /// <summary>A script path that leaves the workspace.</summary>
    public static string ScriptOutside(string path) => $"{path} is not a path inside the working copy; a script runs from the workspace's root.";

    /// <summary>A host whose home the probe did not report, without which a container cannot be given the workspace.</summary>
    public static string HomeUnknown(string name) => $"{name} did not say where its home is, so the workspace cannot be mounted into a container.";

    /// <summary>A Mutagen session that could not be made or caught up.</summary>
    public static string SessionFailed(string session, string reason) => $"The session {session} did not deliver the copy: {reason}";

    /// <summary>What a person does when mutagen is wanted and missing.</summary>
    public const string InstallMutagen = "Install Mutagen on this machine: brew install mutagen-io/mutagen/mutagen; or set Sync to rsync.";

    /// <summary>A fetch that names no path.</summary>
    public const string NothingToFetch = "Name at least one path of the workspace to bring back.";

    /// <summary>A log that is not one of this server's.</summary>
    public static string NotALog(string path, string folder) => $"{path} is not a log of this server; its logs are under {folder}.";

    /// <summary>A job this server does not know.</summary>
    public static string UnknownJob(string uri) => $"No job of this server is {uri}.";

    /// <summary>A simulator that could not be cloned.</summary>
    public static string SimulatorFailed(string device, string reason) => $"No simulator was cloned from \"{device}\": {reason}";

    /// <summary>What a device name must be.</summary>
    public const string SimulatorHint = "Name a simulator the host has, as xcrun simctl list devices shows it, or give the destination an id= instead of a name=.";

    /// <summary>A project that could not be generated from its spec.</summary>
    public static string GenerationFailed(string log) => $"xcodegen did not generate the project; the log says why: {log}";

    /// <summary>Where xcodegen comes from on a host.</summary>
    public const string GenerationHint = "build_provision puts xcodegen under the host's root when Xcode is there.";

    /// <summary>A survey that did not end well.</summary>
    public static string SurveyFailed(string name, string reason) => $"{name} was not surveyed: {reason}";

    /// <summary>A plan this server does not hold any more.</summary>
    public static string UnknownPlan(string planId) => $"No plan is {planId}; a plan is kept for {SweepPlan.Keeps.TotalMinutes:0} minutes after it is shown — ask for it again.";

    /// <summary>A word that is not a scope.</summary>
    public static string UnknownScope(string word) => $"\"{word}\" is not a scope: workspace, repository, caches or everything.";

    /// <summary>A scope that needs a copy or a repository named.</summary>
    public const string ScopeNeedsCopy = "A workspace or repository sweep needs a path inside the working copy, or the repository's name.";

    /// <summary>A command that ran past its time.</summary>
    public static string TimedOut(TimeSpan timeout) => $"The command ran past {timeout.TotalMinutes:0} minutes and was stopped.";
}
