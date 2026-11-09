namespace Snail.MCP.BuildBridge.Tools;

/// <summary>What the model reads before calling: the tool contract, guarded by the tests in <c>ToolCatalogTests</c>.</summary>
/// <remarks>Every number here is a default of <c>BuildOptions</c>, and a guard test fails when the two drift apart.</remarks>
public static class ToolDescriptions
{
    /// <summary>The toolchains a host may have, as the tools spell them; a guard test compares them with <c>Capability</c>.</summary>
    public const string Capabilities = "dotnet, xcode, docker, android and node";

    /// <summary>The contract of build_hosts.</summary>
    public const string Hosts =
        $"""
        List the build hosts this server knows and what each can do right now: whether it answers over ssh, which
        toolchains it has with their versions — {Capabilities} — which .NET SDKs it holds under its root, and how
        many gigabytes are free under its home. A host that does not answer says why. Changes nothing. The first
        host is the one a call that names none gets.
        """;

    /// <summary>The contract of build_sync.</summary>
    public const string Sync =
        """
        Deliver the working copy a path lies in to a host's workspace, or with dryRun list what a delivery would send
        without sending it. The workspace is one per repository and branch under the host's root. Only what changed
        goes over the wire; what git ignores and what a build makes again — bin, obj, artifacts, TestResults — never
        goes, and a tracked file deleted here is deleted there. Build outputs already on the host are left alone, so a
        build there stays incremental. A linked worktree is delivered without its .git. A delivery may take up to 600
        seconds. With Sync set to mutagen in the settings a session keeps the workspace mirrored as the copy changes,
        a delivery only waits for it to catch up and names no files, and dryRun still lists the difference through
        rsync. build_run delivers by itself before it runs, so call this to check a delivery, not before every run.
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
        root with its workloads, add the mirrored package feeds to its NuGet sources, build the images the settings
        name for the linux and android kinds when docker is there, put xcodegen under the root when Xcode is there,
        put a JDK and the Android SDK with its NDK under the root for the gradle and android kinds — whose licences a
        person accepts, and the answer says how — put Node under the root for the npm kind, and look at Xcode,
        Docker, Rosetta and sleep — those a person
        installs or sets, since each means a licence or a password, and the answer says how. Run it on a new host,
        and again after the settings change or a licence is accepted; what is in place already is left as it is. The
        work runs as a job: the answer is the job's report, with the steps as its result when it ends within the wait,
        or the running job, whose resource job://buildbridge/provision/<id> carries them once it ends; a host that does
        not answer or a provisioning that broke off ends the job failed with the error, its type — unreachable,
        notInstalled, timeout, notProvisioned — and a hint when there is something to do. A workload install is minutes, the Android packages and an image longer; the job may run up
        to 3600 seconds.
        """;

    /// <summary>The wait parameter of a tool that runs a job.</summary>
    public const string Wait = "Seconds to wait for the work before answering with the running job; 60 when not given, 0 answers with the job at once.";

    /// <summary>The kinds of build, as the tools spell them; a guard test compares them with <c>BuildKind</c>.</summary>
    public const string Kinds = "dotnet, xcode, docker, linux, android, gradle, npm or script";

    /// <summary>The contract of build_run.</summary>
    public const string Run =
        $"""
        Build, test or publish a working copy on a host and get back how it went: the copy a path lies in is delivered
        first (as build_sync does), the program of the kind runs in its workspace with the arguments given, and the
        answer says whether it succeeded, the exit code, what the tests said when there were any, and where the log
        is on this machine, which build_logs reads. Outputs stay on the host, where the next run finds them;
        build_fetch brings named paths back.

        The kinds — {Kinds}. dotnet runs dotnet on the host, such as test -c Release. xcode runs xcodebuild, such as
        build -scheme App -destination 'platform=iOS Simulator,name=iPhone 17'. docker runs docker, such as compose
        up -d --build. linux runs dotnet inside the host's s3l-build-dotnet image: a Linux target, Native AOT for
        Linux, tests on Linux. android runs dotnet for an Android target: on the host when it has the Android
        toolchain under its root, inside the s3l-build-android image otherwise. gradle runs the repository's gradlew,
        such as assembleDebug, with the JDK, the SDK and the NDK under the root. npm runs npm with Node under the root,
        such as ci, test, run build, or exec -- playwright install, npm's cache and Playwright's browsers kept under
        the root too. script runs a script of the
        repository as it is — the first argument its path from the workspace's root, such as
        tools/native/build-xcframework.sh, the rest its arguments — for what a build needs that no one program says.

        What a build needs beyond the host's comes with the build, not from the settings: needs names Android SDK
        packages, put under the root before the run and kept; the .NET SDK the copy's global.json names goes under
        the root when none there meets it; a dotnet build that fails for want of a platform (XA5207) or of workloads
        (NETSDK1147) gets them and runs again by itself, once; a gradle build fetches what its project names on its
        own. environment assigns variables for the run that are no secret; the secrets the settings name for the
        repository reach the host's shell for the run alone, never a tool, a job or the log, and build_diagnose
        says which names those are.

        An xcode run keeps its derived data under the host's root; each destination that names a simulator by name
        gets a clone of it for the run alone, deleted when the run ends, so several destinations in one run test on
        an iPhone and an iPad side by side; a project with a project.yml beside it is generated on the host by
        xcodegen first. A docker build or run carries the label snail.owner, and a compose stand is the project
        s3l-<repository>-<branch> unless -p names one.

        The work runs as a job: the answer is the job's report, with the build as its result when it ends within the
        wait, or the running job, whose resource job://buildbridge/<kind>/<id> carries it once it ends and whose
        message names the log. A build that ran and failed is a job that succeeded, its result saying success false;
        a run that could not happen — no working copy, a host that does not answer, a delivery that broke off — ends
        the job failed with the error, its type — notACopy, unreachable, noToolchain, notDelivered and the rest the
        server's instructions name — and a hint when there is something to do. A run may take up to 3600 seconds.
        One workspace — a repository on a branch — takes one run at a time, and a second waits its turn; runs in other
        workspaces, another branch or another repository, go side by side.
        """;

    /// <summary>The path parameter of build_run.</summary>
    public const string RunPath = "A path inside the working copy to build, such as the repository's root.";

    /// <summary>The kind parameter of build_run.</summary>
    public const string RunKind = $"Which program runs: {Kinds}.";

    /// <summary>The arguments parameter of build_run.</summary>
    public const string RunArguments = "The program's arguments, one word each, as they would be typed in the workspace's root: [\"test\", \"-c\", \"Release\"].";

    /// <summary>The needs parameter of build_run.</summary>
    public const string RunNeeds = "Android SDK packages this build needs beyond what the host has, as sdkmanager names them — [\"platforms;android-37\", \"build-tools;37.0.0\"]; a platform named by its API level is taken with the minor version Google gives it — put under the host's root before the run and kept there; none when not given.";

    /// <summary>The environment parameter of build_run.</summary>
    public const string RunEnvironment = "Variables for the run that are no secret, NAME=value each, such as [\"ASPNETCORE_ENVIRONMENT=Testing\"]; none when not given. A secret never comes this way: the settings name it under Secrets, and the run gets it from this machine's file or keychain for the repository it is scoped to.";

    /// <summary>The contract of build_logs.</summary>
    public const string Logs =
        """
        Read the end of a build's log on this machine, as build_run named it, while the build runs or after it ended.
        Only a log of this server is read: a path that leads out of its jobs folder, links followed, is refused as
        outside, and a log that is not there as absent. The last 100 lines unless asked for more, at most 2000.
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
        branch), every workspace of a repository, what has gone stale — workspaces not built for 14 days, Xcode's
        derived data and test bundles inside a copy nothing wrote into for 7 days, derived data of workspaces that
        are gone, images older than 7 days, build cache not used for 6 hours, logs older than 30 days, and the
        versions an update left behind: SDKs no settings and no workspace's global.json name, build tools and NDKs
        older than the newest unless the settings name them, toolchain images the settings do not name — or
        everything under the root but the SDK. A plan is kept 10 minutes. build_run sweeps by itself: after each run
        a repository's stale workspaces, the quiet outputs inside its copies and the build cache past its hours, and
        the caches before one when the host has less than 20 GB free.
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
        Say what state this server is in: its version, the hosts its settings name, whether ssh, rsync, git and
        mutagen are on this machine, what carries a copy, the secrets the settings name — by name, source and
        repository, never their values — and where it keeps its data. Changes nothing. Start here when a build does
        not go where it should.
        """;
}
