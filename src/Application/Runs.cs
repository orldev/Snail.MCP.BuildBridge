using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>A build of a working copy on a host: the copy delivered, the feeds mirrored, the command run in the workspace, the log read for what the tests said.</summary>
/// <remarks>One workspace takes one run at a time, through <see cref="WorkspaceTurns"/>: a run that finds the workspace busy waits its turn, and says so in its progress.</remarks>
public sealed class Runs(Hosts hosts, ICopies copies, IBuilder builder, Logs logs, Sweeps sweeps, WorkspaceTurns turns, ISecrets secrets, IOptions<BuildOptions> options)
{

    /// <summary>Runs <paramref name="kind"/>'s program with <paramref name="arguments"/> in the workspace of the copy <paramref name="path"/> lies in, on the host named or the first, with the Android SDK packages <paramref name="needs"/> names put under its root first, and the variables <paramref name="environment"/> assigns and the secrets of the repository in the command's environment.</summary>
    public async Task<Answer<BuildReport>> RunAsync(string path, BuildKind kind, IReadOnlyList<string> arguments, IReadOnlyList<string> needs, IReadOnlyList<string> environment, string? host, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        var copy = await copies.ReadAsync(path, cancellationToken);
        if (!copy.TryGet(out var found))
            return copy.As<BuildReport>();

        var (named, misnamed) = Variables.Parsed(environment);
        if (misnamed is not null)
            return Answer.Failed(Messages.NotAVariable(misnamed), type: Refusals.Arguments);
        var secret = await secrets.ForAsync(found.Repository, cancellationToken);
        if (!secret.TryGet(out var hidden))
            return secret.As<BuildReport>();
        var variables = Merged(named, hidden);

        if (kind == BuildKind.Script && ScriptOf(found, arguments) is { IsSuccess: false } refused)
            return refused.As<BuildReport>();
        if (kind == BuildKind.Gradle && !File.Exists(Path.Combine(found.Path, GradleCommand.Wrapper)))
            return Answer.Failed(Messages.NoGradleWrapper(found.Path), type: Refusals.Absent);
        if (needs.FirstOrDefault(package => !AndroidPackages.IsName(package)) is { } odd)
            return Answer.Failed(Messages.NotAPackage(odd), type: Refusals.Arguments);

        var ready = await hosts.ReadyAsync(host, kind.Needs(), cancellationToken);
        if (!ready.TryGet(out var state))
            return ready.As<BuildReport>();
        if (needs.Count > 0 && !state.Has(Capability.Android))
            return Answer.Failed(Messages.HostCannot(state.Host.Name, "android"), type: Refusals.NoToolchain);

        string? image = kind.ImageOn(state);
        var supplied = new List<string>();
        foreach (var supply in await WantedAsync(found, kind, image, state, needs, cancellationToken))
        {
            var put = await SuppliedAsync(state.Host, supply, progress, cancellationToken);
            if (!put.TryGet(out var words))
                return put.As<BuildReport>();

            supplied.AddRange(words);
        }

        using (await turns.TakeAsync(state.Host, found.Workspace, () => progress.Report(new JobProgress(0, 1, $"waiting for {found.Workspace.Folder} on {state.Host.Name}, where another run is going")), cancellationToken))
        {
            var swept = await sweeps.BeforeRunAsync(state, cancellationToken);
            if (swept.Count > 0)
                progress.Report(new JobProgress(0, 5, $"swept {state.Host.Name}, which was short of room"));

            var ran = await RanAsync(state, image, found, kind, arguments, variables, progress, cancellationToken);
            if (!ran.TryGet(out var report))
                return ran;

            var after = await sweeps.AfterRunAsync(state.Host, found.Repository, cancellationToken);

            return Answer.Ok(report with { Swept = [.. swept, .. after], Supplied = [.. supplied, .. report.Supplied] });
        }
    }

    /// <summary>The run's variables: what the call assigned, with the secrets of this machine over them, so a call never stands in for a secret.</summary>
    private static Dictionary<string, string> Merged(IReadOnlyDictionary<string, string> named, IReadOnlyDictionary<string, string> hidden)
    {
        var variables = new Dictionary<string, string>(named, StringComparer.Ordinal);
        foreach (var (name, value) in hidden)
            variables[name] = value;

        return variables;
    }

    /// <summary>What a run puts under the root before it starts: the packages the call named, and the SDK the copy's global.json wishes for when none under the root meets it.</summary>
    private static async Task<IReadOnlyList<Supply>> WantedAsync(LocalCopy copy, BuildKind kind, string? image, HostState state, IReadOnlyList<string> needs, CancellationToken cancellationToken)
    {
        var supplies = new List<Supply>();
        if (needs.Count > 0)
            supplies.Add(new Supply(SupplyKind.AndroidPackages, needs));

        if (!RunsDotnetOnHost(kind, image))
            return supplies;

        var wish = await SdkWishOfAsync(copy, cancellationToken);
        if (wish is not null && !wish.IsMetBy(state.Sdks))
            supplies.Add(new Supply(SupplyKind.Sdk, [wish.Version]));

        return supplies;
    }

    /// <summary>The SDK the copy's global.json wishes for; null when it has none, or names no version.</summary>
    public static async Task<SdkWish?> SdkWishOfAsync(LocalCopy copy, CancellationToken cancellationToken)
    {
        string file = Path.Combine(copy.Path, SdkWish.FileName);
        if (!File.Exists(file))
            return null;

        return SdkWish.In(await File.ReadAllTextAsync(file, cancellationToken));
    }

    /// <summary>Whether a run drives dotnet in the host's own folders, where the root's SDKs and workloads are what it builds with.</summary>
    private static bool RunsDotnetOnHost(BuildKind kind, string? image) => image is null && kind is BuildKind.Dotnet or BuildKind.Android or BuildKind.Script;

    /// <summary>A supply put under the host's root, one caller at a time per installer.</summary>
    private async Task<Answer<IReadOnlyList<string>>> SuppliedAsync(Host host, Supply supply, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new JobProgress(0, 5, $"supplying {string.Join(' ', supply.Words)} on {host.Name}"));
        using (await turns.TakeAsync(host, supply.Turn, () => progress.Report(new JobProgress(0, 5, $"waiting for the installer on {host.Name}, where another run drives it")), cancellationToken))
        {
            return await builder.SupplyAsync(host, supply, cancellationToken);
        }
    }

    private async Task<Answer<BuildReport>> RanAsync(HostState state, string? image, LocalCopy copy, BuildKind kind, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> variables, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        var host = state.Host;
        if (image is not null && state.Home is null)
            return Answer.Failed(Messages.HomeUnknown(host.Name), type: Refusals.NoHome);

        progress.Report(new JobProgress(1, 5, $"delivering {copy.Repository} ({copy.Branch}) to {host.Name}"));
        var delivered = await builder.DeliverAsync(host, copy, false, cancellationToken);
        if (!delivered.TryGet(out var delivery))
            return delivered.As<BuildReport>();

        progress.Report(new JobProgress(2, 5, "mirroring the feeds"));
        foreach (var feed in options.Value.Feeds)
        {
            var mirrored = await builder.MirrorAsync(host, feed.Path, $"feeds/{feed.Name}", cancellationToken);
            if (!mirrored.IsSuccess)
                return mirrored.As<BuildReport>();
        }

        var shaped = Shaped(kind, image, arguments, copy.Workspace, state.Home is { } home ? host.RootOn(home) : string.Empty, options.Value.SdkVersion, [.. variables.Keys]);
        string log = logs.New(ToolWord(kind));
        var timeout = TimeSpan.FromSeconds(options.Value.RunSeconds);
        if (kind == BuildKind.Xcode && SpecFolderOf(copy, shaped) is { } folder)
        {
            progress.Report(new JobProgress(3, 5, $"generating the project from {folder}/{XcodeCommand.Spec}"));
            var generated = await builder.ExecuteAsync(host, copy.Workspace, XcodeCommand.Generation(folder), variables, log, timeout, cancellationToken);
            if (!generated.TryGet(out var generation))
                return generated.As<BuildReport>();
            if (!generation.IsSuccess)
                return Answer.Failed(Messages.GenerationFailed(log), Messages.GenerationHint, Refusals.NotGenerated);
        }

        var simulators = new List<string>();
        try
        {
            foreach (var device in kind == BuildKind.Xcode ? XcodeCommand.SimulatorsNamed(shaped) : [])
            {
                progress.Report(new JobProgress(3, 5, $"cloning the simulator {device}"));
                var acquired = await builder.AcquireSimulatorAsync(host, device, $"{XcodeCommand.SimulatorPrefix}{Path.GetRandomFileName()[..6]}", cancellationToken);
                if (!acquired.TryGet(out var udid))
                    return acquired.As<BuildReport>();

                simulators.Add(udid);
            }
            shaped = XcodeCommand.OnDevices(shaped, simulators);

            List<string> command = kind.Program(image) is { } program ? [program, .. shaped] : [.. shaped];
            progress.Report(new JobProgress(4, 5, $"running {string.Join(' ', command)}; log {log}"));

            var executed = await builder.ExecuteAsync(host, copy.Workspace, command, variables, log, timeout, cancellationToken);
            if (!executed.TryGet(out var execution))
                return executed.As<BuildReport>();

            var supplied = await LackingAsync(state, kind, image, copy.Workspace, execution, progress, cancellationToken);
            if (supplied.Count > 0)
            {
                progress.Report(new JobProgress(4, 5, $"running {string.Join(' ', command)} again, with {string.Join(' ', supplied)} in place"));
                executed = await builder.ExecuteAsync(host, copy.Workspace, command, variables, log, timeout, cancellationToken);
                if (!executed.TryGet(out var again))
                    return executed.As<BuildReport>();

                execution = again with { Took = execution.Took + again.Took };
            }

            var tests = TestSummary.Parse(await File.ReadAllTextAsync(log, cancellationToken));

            return Answer.Ok(new BuildReport(host, kind, command, delivery, execution, tests, []) { Supplied = supplied });
        }
        finally
        {
            foreach (var udid in simulators)
                await builder.ReleaseSimulatorAsync(host, udid, CancellationToken.None);
        }
    }

    /// <summary>What a dotnet build on the host failed for want of — a platform, workloads — put under the root so the build can run again; nothing when the build went well, ran in a container, or failed for another reason.</summary>
    /// <remarks>Once: a build that still lacks it after it was supplied has another problem, which its log shows.</remarks>
    private async Task<IReadOnlyList<string>> LackingAsync(HostState state, BuildKind kind, string? image, WorkspaceKey key, Execution execution, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        if (!RunsDotnetOnHost(kind, image) || execution.IsSuccess)
            return [];

        string tail = string.Join('\n', await Logs.TailAsync(execution.Log, 200, cancellationToken));
        if (Lacks.In(tail) is not { } lack)
            return [];

        var supplied = await SuppliedAsync(state.Host, lack with { Folder = key.CopyFolder }, progress, cancellationToken);

        return supplied.TryGet(out var words) ? words : [];
    }

    /// <summary>The arguments as the kind shapes them for the workspace: docker run of <paramref name="image"/> around dotnet when the run is in a container; otherwise derived data under the root for xcodebuild, the owner label and the project for docker, the script's path for a script, and as given for the rest.</summary>
    public static IReadOnlyList<string> Shaped(BuildKind kind, string? image, IReadOnlyList<string> arguments, WorkspaceKey key, string root, string sdkVersion, IReadOnlyCollection<string> variables)
    {
        if (image is not null)
            return ContainerCommand.Shaped(kind, arguments, key, root, sdkVersion, variables);

        return kind switch
        {
            BuildKind.Xcode => XcodeCommand.Shaped(arguments, key),
            BuildKind.Docker => DockerCommand.Shaped(arguments, key),
            BuildKind.Script => ScriptCommand.Shaped(arguments),
            _ => arguments
        };
    }

    /// <summary>The script a script run means, checked against the copy before anything goes to the host: named, inside the copy, and there.</summary>
    public static Answer<string> ScriptOf(LocalCopy copy, IReadOnlyList<string> arguments)
    {
        if (ScriptCommand.PathOf(arguments) is not { } path)
            return Answer.Failed(Messages.NoScriptNamed, type: Refusals.Arguments);
        if (!ScriptCommand.IsInside(path))
            return Answer.Failed(Messages.ScriptOutside(path), type: Refusals.Outside);
        if (!File.Exists(Path.Combine(copy.Path, path)))
            return Answer.Failed(Messages.NoScript(path, copy.Path), type: Refusals.Absent);

        return Answer.Ok(path);
    }

    /// <summary>The folder of the spec a project is generated from, when the copy has one beside the project the arguments name; null when the project is checked in as it is.</summary>
    /// <remarks>A generated project is what git ignores, so it never reaches the host; the spec does, and XcodeGen on the host makes the project from it before every build, which is cheap and always current.</remarks>
    public static string? SpecFolderOf(LocalCopy copy, IReadOnlyList<string> arguments)
    {
        string folder = XcodeCommand.ProjectFolderOf(arguments);
        if (!ScriptCommand.IsInside(folder))
            return null;

        return File.Exists(Path.Combine(copy.Path, folder, XcodeCommand.Spec)) ? folder : null;
    }

    /// <summary>A kind as the tools spell it.</summary>
    public static string ToolWord(BuildKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The kind a tool was given, or why the word is not one.</summary>
    public static Answer<BuildKind> KindOf(string word) =>
        Enum.TryParse<BuildKind>(word, true, out var kind) && Enum.IsDefined(kind)
            ? Answer.Ok(kind)
            : Answer.Failed(Messages.UnknownKind(word), type: Refusals.Arguments);
}
