namespace Snail.MCP.BuildBridge.Tools;

/// <summary>What the model reads before calling: the tool contract, guarded by the tests in <c>ToolCatalogTests</c>.</summary>
/// <remarks>Every number here is a default of <c>BuildOptions</c>, and a guard test fails when the two drift apart.</remarks>
public static class ToolDescriptions
{
    /// <summary>The toolchains a host may have, as the tools spell them; a guard test compares them with <c>Capability</c>.</summary>
    public const string Capabilities = "dotnet, xcode and docker";

    /// <summary>The contract of build_hosts.</summary>
    public const string Hosts =
        $"""
        List the build hosts this server knows and what each can do right now: whether it answers over ssh, which
        toolchains it has with their versions — {Capabilities} — and how many gigabytes are free under its home. A host
        that does not answer says why. Changes nothing. The first host is the one a call that names none gets.
        """;

    /// <summary>The contract of build_sync.</summary>
    public const string Sync =
        """
        Deliver the working copy a path lies in to a host's workspace, or with dryRun list what a delivery would send
        without sending it. The workspace is one per repository and branch under the host's root. Only what changed
        goes over the wire; what git ignores and what a build makes again — bin, obj, artifacts, TestResults — never
        goes, and a tracked file deleted here is deleted there. Build outputs already on the host are left alone, so a
        build there stays incremental. A linked worktree is delivered without its .git. A delivery may take up to 600
        seconds. build_run delivers by itself before it runs, so call this to check a delivery, not before every run.
        """;

    /// <summary>The path parameter of build_sync.</summary>
    public const string SyncPath = "A path inside the working copy to deliver, such as the repository's root.";

    /// <summary>The host parameter of a tool that names one.</summary>
    public const string HostName = "The host to use, by the name build_hosts lists; the first host when not given.";

    /// <summary>The dryRun parameter of build_sync.</summary>
    public const string SyncDryRun = "List what would be sent and send nothing; false when not given.";

    /// <summary>The contract of build_provision.</summary>
    public const string Provision =
        """
        Make a host ready to build: create its root with its folders, install the .NET SDK the settings name under the
        root with its workloads, add the mirrored package feeds to its NuGet sources, and look at Xcode, Docker and
        sleep — those a person installs or sets, and the answer says how. Run it on a new host, and again after the
        settings change; what is in place already is left as it is. The work runs as a job: the answer is the job's
        report, with the steps as its result when it ends within the wait, or the running job, whose resource
        job://buildbridge/provision/<id> carries them once it ends. A workload install is minutes; the job may run up
        to 3600 seconds.
        """;

    /// <summary>The wait parameter of a tool that runs a job.</summary>
    public const string Wait = "Seconds to wait for the work before answering with the running job; 60 when not given, 0 answers with the job at once.";

    /// <summary>The kinds of build, as the tools spell them; a guard test compares them with <c>BuildKind</c>.</summary>
    public const string Kinds = "dotnet, xcode or docker";

    /// <summary>The contract of build_run.</summary>
    public const string Run =
        $"""
        Build, test or publish a working copy on a host and get back how it went. The copy a path lies in is delivered
        first (as build_sync does), then the program of the kind — {Kinds}: dotnet, xcodebuild or docker — runs in the
        workspace with the arguments given, such as test -c Release, or build -scheme App -destination
        'platform=iOS Simulator,name=iPhone 17', or compose up -d --build. The answer says whether it succeeded, the
        exit code, what the tests said when there were any, and where the log is on this machine, which build_logs
        reads. Outputs stay on the host, where the next run finds them; build_fetch brings named paths back. An
        xcodebuild run keeps its derived data under the host's root, and a destination that names a simulator by name
        gets a clone of it for the run alone, deleted when the run ends. A docker build or run carries the label
        snail.owner, and a compose stand is the project s3l-<repository>-<branch> unless -p names one. The work
        runs as a job: the answer is the job's report, with the build as its result when it ends within the wait, or
        the running job, whose resource job://buildbridge/<kind>/<id> carries it once it ends and whose message names
        the log. A run may take up to 3600 seconds; one workspace takes one run at a time, and a second waits its turn.
        """;

    /// <summary>The path parameter of build_run.</summary>
    public const string RunPath = "A path inside the working copy to build, such as the repository's root.";

    /// <summary>The kind parameter of build_run.</summary>
    public const string RunKind = $"Which program runs: {Kinds}.";

    /// <summary>The arguments parameter of build_run.</summary>
    public const string RunArguments = "The program's arguments, one word each, as they would be typed in the workspace's root: [\"test\", \"-c\", \"Release\"].";

    /// <summary>The contract of build_logs.</summary>
    public const string Logs =
        """
        Read the end of a build's log on this machine, as build_run named it, while the build runs or after it ended.
        Only a log of this server is read. The last 100 lines unless asked for more, at most 2000.
        """;

    /// <summary>The log parameter of build_logs.</summary>
    public const string LogsLog = "The log's path, as build_run's answer or its job's message names it.";

    /// <summary>The lines parameter of build_logs.</summary>
    public const string LogsLines = "How many lines from the end to read; 100 when not given, at most 2000.";

    /// <summary>The contract of build_cancel.</summary>
    public const string Cancel =
        """
        Stop a running job of this server — a build or a provisioning — by the job resource build_run or build_provision
        answered with: the command is stopped on the host too. A job that ended already is left as it is.
        """;

    /// <summary>The job parameter of build_cancel.</summary>
    public const string CancelJob = "The job's resource, job://buildbridge/<kind>/<id>.";

    /// <summary>The contract of build_fetch.</summary>
    public const string Fetch =
        """
        Bring named paths of a workspace back to this machine: test results, a bundle for a simulator, a package, a
        published folder. Paths are relative to the workspace's root, such as tests/TestResults or
        src/bin/Release/net10.0/publish; they land under their own path inside the folder named, or inside this
        server's data folder by repository and branch when none is. Only what changed goes over the wire.
        """;

    /// <summary>The path parameter of build_fetch.</summary>
    public const string FetchPath = "A path inside the working copy whose workspace is read, such as the repository's root.";

    /// <summary>The paths parameter of build_fetch.</summary>
    public const string FetchPaths = "The paths to bring back, relative to the workspace's root.";

    /// <summary>The into parameter of build_fetch.</summary>
    public const string FetchInto = "The folder on this machine they land in; this server's data folder, by repository and branch, when not given.";

    /// <summary>The scopes of a sweep, as the tools spell them; a guard test compares them with <c>SweepScope</c>.</summary>
    public const string Scopes = "workspace, repository, caches or everything";

    /// <summary>The contract of build_clean.</summary>
    public const string Clean =
        $"""
        Give a host its room back, in two calls: first a plan of a scope is decided and shown under a planId, then that
        plan, and only it, is carried out when called again with the planId. Nothing on a host is the only copy of
        anything, so a sweep erases. The scopes are {Scopes}: one workspace (the copy at path, or repository and
        branch), every workspace of a repository, what has gone stale — workspaces not built for 14 days, derived
        data of workspaces that are gone, images older than 7 days, build cache not used for 24 hours, logs older
        than 30 days — or everything under the root but the SDK. A plan is kept 10 minutes. build_run sweeps by itself:
        a repository's stale workspaces after each run, and the caches before one when the host has less than 20 GB
        free.
        """;

    /// <summary>The scope parameter of build_clean.</summary>
    public const string CleanScope = $"How much is looked at: {Scopes}.";

    /// <summary>The path parameter of build_clean.</summary>
    public const string CleanPath = "A path inside the working copy whose workspace or repository is meant; not needed for caches or everything.";

    /// <summary>The repository parameter of build_clean.</summary>
    public const string CleanRepository = "The repository's name, instead of a path, for a repository sweep.";

    /// <summary>The planId parameter of build_clean.</summary>
    public const string CleanPlan = "The planId of a plan shown before, to carry it out; not given, a new plan is shown.";

    /// <summary>The contract of build_diagnose.</summary>
    public const string Diagnose =
        """
        Say what state this server is in: its version, the hosts its settings name, whether ssh, rsync and git are on
        this machine, and where it keeps its data. Changes nothing. Start here when a build does not go where it should.
        """;
}
