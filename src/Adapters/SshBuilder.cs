using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>A host worked over ssh and rsync, with nothing of this server's installed there: a probe, a delivery, a run, a collection and a removal are each a script or an rsync.</summary>
/// <remarks>
/// <para>
/// A delivery mirrors the copy with rsync: what git ignores and what a build makes again are excluded, and an excluded
/// folder already on the host — <c>bin</c>, <c>obj</c> — is left alone, so a build there stays incremental. A tracked
/// file deleted here is deleted there. The marker written beside the copy is what proves a workspace this server's.
/// </para>
/// <para>
/// A run's command is started in the background of its script and its id written beside the workspace, so a run
/// stopped from this side — its ssh killed on a timeout — can be stopped on the host too, where killing the ssh
/// client alone would leave the build running.
/// </para>
/// </remarks>
public sealed class SshBuilder(IPrograms programs, ICopies copies, RemoteShell shell, IOptions<BuildOptions> options, TimeProvider clock) : IBuilder
{
    private const string PidFileName = ".s3l-build.pid";

    /// <inheritdoc/>
    public async Task<HostState> ProbeAsync(Host host, CancellationToken cancellationToken)
    {
        const string script = """
            printf 'dotnet=%s\n' "$(dotnet --version 2>/dev/null)"
            printf 'xcode=%s\n' "$(xcodebuild -version 2>/dev/null | head -n 1 | sed 's/^Xcode //')"
            printf 'docker=%s\n' "$(docker version --format '{{.Server.Version}}' 2>/dev/null)"
            printf 'free=%s\n' "$(df -g "$HOME" | tail -n 1 | awk '{print $4}')"
            """;

        var run = await RunScriptAsync(host, script, cancellationToken);
        if (run.IsMissing)
            return HostState.Unreachable(host, Messages.ProgramMissing(options.Value.Ssh));
        if (run.IsTimedOut)
            return HostState.Unreachable(host, Messages.HostDown(host.Name, host.Address, Messages.TimedOut(TimeSpan.FromSeconds(options.Value.ProbeSeconds))));
        if (!run.IsSuccess)
            return HostState.Unreachable(host, Messages.HostDown(host.Name, host.Address, run.Error.Trim()));

        var answers = Parsed(run.Output);
        var toolchains = new List<Toolchain>
        {
            new(Capability.Dotnet, Version(answers, "dotnet")),
            new(Capability.Xcode, Version(answers, "xcode")),
            new(Capability.Docker, Version(answers, "docker"))
        };
        double? free = double.TryParse(answers.GetValueOrDefault("free"), NumberStyles.Float, CultureInfo.InvariantCulture, out double gigabytes) ? gigabytes : null;

        return new HostState(host, true, toolchains, free, null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The script says what it saw to in lines of its own among the installers' output — <c>s3l-step name 1|0 note</c> —
    /// and those are read back from the log. The SDK comes from dotnet-install.sh into the root; a workload install is
    /// minutes, so only the missing ones are asked for. Xcode, Docker and sleep are only looked at: each is a person's
    /// to install or set, and the answer says how.
    /// </remarks>
    public async Task<Answer<Provisioned>> ProvisionAsync(Host host, BuilderManifest manifest, string log, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var words = shell.Command(host, RemoteShell.Prelude(host) + ProvisionScript(host, manifest));
        var run = await programs.RunToLogAsync(words[0], words[1..], null, log, timeout, cancellationToken);

        if (run.IsMissing)
            return Answer.Failed<Provisioned>(Messages.ProgramMissing(words[0]));
        if (run.IsTimedOut)
            return Answer.Failed<Provisioned>(Messages.ProvisionFailed(host.Name, Messages.TimedOut(timeout)));

        var steps = Steps(await File.ReadAllTextAsync(log, cancellationToken));
        if (steps.Count == 0)
            return Answer.Failed<Provisioned>(Messages.ProvisionFailed(host.Name, $"exit code {run.ExitCode}; see {log}"));

        return Answer.Ok(new Provisioned(host, steps, log));
    }

    /// <inheritdoc/>
    public async Task<Answer<Delivery>> DeliverAsync(Host host, LocalCopy copy, bool isDryRun, CancellationToken cancellationToken)
    {
        var key = copy.Workspace;
        var made = await RunScriptAsync(host, $"mkdir -p {RemoteShell.Under(host, key.Folder)}", cancellationToken);
        if (!made.IsSuccess)
            return Answer.Failed<Delivery>(Messages.DeliveryFailed(host.Name, Reason(made)));

        var ignored = await copies.IgnoredAsync(copy, cancellationToken);
        string excludes = Excludes(copy, ignored);
        var started = clock.GetTimestamp();

        var arguments = RsyncArguments(host, isDryRun, true, $"{copy.Path}/", $"{RemoteShell.Rsync(host, key.Folder)}/");
        var run = await programs.RunAsync(options.Value.Rsync, arguments, null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken, excludes);
        if (run.IsMissing)
            return Answer.Failed<Delivery>(Messages.ProgramMissing(options.Value.Rsync));
        if (!run.IsSuccess)
            return Answer.Failed<Delivery>(Messages.DeliveryFailed(host.Name, Reason(run)));

        if (!isDryRun)
            await MarkAsync(host, new WorkspaceMarker(key, $"{Environment.MachineName}:{copy.Path}", copy.Commit, clock.GetUtcNow(), null), cancellationToken);

        return Answer.Ok(new Delivery(key, Changed(run.Output), TransferredBytes(run.Output), clock.GetElapsedTime(started), isDryRun));
    }

    /// <inheritdoc/>
    public async Task<Answer<Delivery>> MirrorAsync(Host host, string source, string relative, CancellationToken cancellationToken)
    {
        var key = new WorkspaceKey(relative, string.Empty);
        var made = await RunScriptAsync(host, $"mkdir -p {RemoteShell.Under(host, relative)}", cancellationToken);
        if (!made.IsSuccess)
            return Answer.Failed<Delivery>(Messages.DeliveryFailed(host.Name, Reason(made)));

        var started = clock.GetTimestamp();
        var arguments = RsyncArguments(host, false, false, $"{source.TrimEnd('/')}/", $"{RemoteShell.Rsync(host, relative)}/");
        var run = await programs.RunAsync(options.Value.Rsync, arguments, null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken);
        if (run.IsMissing)
            return Answer.Failed<Delivery>(Messages.ProgramMissing(options.Value.Rsync));
        if (!run.IsSuccess)
            return Answer.Failed<Delivery>(Messages.DeliveryFailed(host.Name, Reason(run)));

        return Answer.Ok(new Delivery(key, Changed(run.Output), TransferredBytes(run.Output), clock.GetElapsedTime(started), false));
    }

    /// <inheritdoc/>
    public async Task<Answer<Execution>> ExecuteAsync(Host host, WorkspaceKey key, IReadOnlyList<string> command, string log, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string workspace = RemoteShell.Under(host, key.Folder);
        string pidFile = RemoteShell.Under(host, $"{key.Folder}/{PidFileName}");
        string script = $"""
            {RemoteShell.Prelude(host)}cd {workspace} || exit 97
            touch {RemoteShell.Under(host, $"{key.Folder}/{WorkspaceMarker.BuiltFileName}")}
            {RemoteShell.Quoted(command)} &
            pid=$!
            echo "$pid" > {pidFile}
            wait "$pid"
            status=$?
            rm -f {pidFile}
            exit "$status"
            """;

        var words = shell.Command(host, script);
        var started = clock.GetTimestamp();
        ProgramRun run;

        try
        {
            run = await programs.RunToLogAsync(words[0], words[1..], null, log, timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(host, pidFile);

            throw;
        }

        if (run.IsMissing)
            return Answer.Failed<Execution>(Messages.ProgramMissing(words[0]));
        if (run.IsTimedOut)
            await StopAsync(host, pidFile);

        return Answer.Ok(new Execution(run.ExitCode, log, clock.GetElapsedTime(started), run.IsTimedOut));
    }

    /// <inheritdoc/>
    /// <remarks>simctl answers a clone with the new device's id alone on its standard output, and a device it does not know with "Invalid device".</remarks>
    public async Task<Answer<string>> AcquireSimulatorAsync(Host host, string device, string clone, CancellationToken cancellationToken)
    {
        var run = await RunScriptAsync(host, $"xcrun simctl clone {RemoteShell.Quoted(device)} {RemoteShell.Quoted(clone)}", cancellationToken, TimeSpan.FromSeconds(options.Value.TransferSeconds));
        if (!run.IsSuccess)
            return Answer.Failed<string>(Messages.SimulatorFailed(device, Reason(run)), Messages.SimulatorHint);

        string udid = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

        return udid.Length > 0 ? Answer.Ok(udid) : Answer.Failed<string>(Messages.SimulatorFailed(device, "simctl named no device"), Messages.SimulatorHint);
    }

    /// <inheritdoc/>
    public async Task ReleaseSimulatorAsync(Host host, string udid, CancellationToken cancellationToken)
    {
        string quoted = RemoteShell.Quoted(udid);

        await RunScriptAsync(host, $"xcrun simctl shutdown {quoted} >/dev/null 2>&1; xcrun simctl delete {quoted}", cancellationToken, TimeSpan.FromSeconds(options.Value.TransferSeconds));
    }

    /// <inheritdoc/>
    public async Task<Answer<Collected>> CollectAsync(Host host, WorkspaceKey key, IReadOnlyList<string> paths, string folder, CancellationToken cancellationToken)
    {
        var astray = paths.FirstOrDefault(path => Path.IsPathRooted(path) || path.Split('/').Contains(".."));
        if (astray is not null)
            return Answer.Failed<Collected>(Messages.CollectionFailed(host.Name, $"{astray} is not a path inside the workspace."));

        long bytes = 0;
        foreach (var path in paths)
        {
            string into = Path.Combine(folder, Path.GetDirectoryName(path) ?? string.Empty);
            Directory.CreateDirectory(into);

            var arguments = RsyncArguments(host, false, false, RemoteShell.Rsync(host, $"{key.Folder}/{path}"), $"{into}/");
            var run = await programs.RunAsync(options.Value.Rsync, arguments, null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken);
            if (run.IsMissing)
                return Answer.Failed<Collected>(Messages.ProgramMissing(options.Value.Rsync));
            if (!run.IsSuccess)
                return Answer.Failed<Collected>(Messages.CollectionFailed(host.Name, Reason(run)));

            bytes += TransferredBytes(run.Output);
        }

        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file))
            .Order(StringComparer.Ordinal)
            .ToList();

        return Answer.Ok(new Collected(folder, files, bytes));
    }

    /// <inheritdoc/>
    public async Task<Answer<Survey>> SurveyAsync(Host host, CancellationToken cancellationToken)
    {
        var run = await RunScriptAsync(host, SurveyScript(host), cancellationToken, TimeSpan.FromSeconds(options.Value.TransferSeconds));
        if (!run.IsSuccess)
            return Answer.Failed<Survey>(Messages.SurveyFailed(host.Name, Reason(run)));

        return Answer.Ok(SurveyOf(host, run.Output));
    }

    /// <inheritdoc/>
    public async Task<Answer<bool>> CarryOutAsync(Host host, SweepItem item, CancellationToken cancellationToken)
    {
        if (item.Kind == SweepKind.Workspace)
        {
            var parts = item.Name.Split('/', 2);

            return parts.Length == 2 ? await RemoveAsync(host, new WorkspaceKey(parts[0], parts[1]), cancellationToken) : Answer.Ok(false);
        }

        string script = item.Kind switch
        {
            SweepKind.Derived when item.Name == WorkspaceKey.Safe(item.Name) => $"rm -rf {RemoteShell.Under(host, $"derived/{item.Name}")} && echo removed",
            SweepKind.Images => $"docker image prune -a --force --filter label={DockerCommand.Label} {(item.Name == "all" ? string.Empty : $"--filter until={item.Name}")} >/dev/null && echo removed",
            SweepKind.BuildCache => $"docker builder prune -a --force {(item.Name == "all" ? string.Empty : $"--filter until={item.Name}")} >/dev/null && echo removed",
            SweepKind.Packages => $"rm -rf {RemoteShell.Under(host, "nuget")} && echo removed",
            _ => string.Empty
        };
        if (script.Length == 0)
            return Answer.Ok(false);

        var run = await RunScriptAsync(host, script, cancellationToken, TimeSpan.FromMinutes(30));
        if (!run.IsSuccess)
            return Answer.Failed<bool>(Reason(run));

        return Answer.Ok(run.Output.Contains("removed", StringComparison.Ordinal));
    }

    /// <summary>The script that surveys a root: a line per workspace with its marker, per derived folder, per labelled image, then the build cache, the packages and the room.</summary>
    internal static string SurveyScript(Host host) =>
        $$$"""
        ROOT={{{RemoteShell.Root(host)}}}
        for marker in "$ROOT"/workspaces/*/*/{{{WorkspaceMarker.FileName}}}; do
          [ -f "$marker" ] || continue
          dir=$(dirname "$marker"); repo=$(basename "$(dirname "$dir")"); branch=$(basename "$dir")
          delivered=$(stat -f %m "$marker"); built=0
          [ -f "$dir/{{{WorkspaceMarker.BuiltFileName}}}" ] && built=$(stat -f %m "$dir/{{{WorkspaceMarker.BuiltFileName}}}")
          echo "s3l-ws $repo $branch $delivered $built $(du -sk "$dir" | cut -f1)"
        done
        for folder in "$ROOT"/derived/*/; do [ -d "$folder" ] && echo "s3l-derived $(basename "$folder")"; done
        docker image ls --filter label={{{DockerCommand.Label}}} --format 's3l-image {{.ID}} {{.CreatedAt}} {{.Size}}' 2>/dev/null
        docker system df --format '{{.Type}}	{{.Size}}' 2>/dev/null | awk -F '	' '$1 == "Build Cache" { print "s3l-buildcache " $2 }'
        echo "s3l-packages $(du -sk "$ROOT/nuget" 2>/dev/null | cut -f1)"
        echo "s3l-free $(df -g "$HOME" | tail -n 1 | awk '{print $4}')"
        """;

    /// <summary>A survey read from the script's lines.</summary>
    internal static Survey SurveyOf(Host host, string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split(' ')).ToList();
        var workspaces = lines.Where(parts => parts[0] == "s3l-ws" && parts.Length == 6).Select(Workspace).OfType<WorkspaceState>().ToList();
        var derived = lines.Where(parts => parts[0] == "s3l-derived" && parts.Length == 2).Select(parts => parts[1]).ToList();
        var images = lines.Where(parts => parts[0] == "s3l-image" && parts.Length >= 6).Select(Image).OfType<ImageState>().ToList();
        double cache = lines.Where(parts => parts[0] == "s3l-buildcache" && parts.Length == 2).Select(parts => Gigabytes(parts[1])).FirstOrDefault();
        double packages = lines.Where(parts => parts[0] == "s3l-packages" && parts.Length == 2).Select(parts => Kilobytes(parts[1]) / 1024d / 1024d).FirstOrDefault();
        double? free = lines.Where(parts => parts[0] == "s3l-free" && parts.Length == 2).Select(parts => double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : (double?)null).FirstOrDefault();

        return new Survey(host, workspaces, derived, images, cache, packages, free);
    }

    private static WorkspaceState? Workspace(string[] parts)
    {
        if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long delivered) || !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long built))
            return null;

        var lastUsed = DateTimeOffset.FromUnixTimeSeconds(Math.Max(delivered, built));

        return new WorkspaceState(new WorkspaceKey(parts[1], parts[2]), lastUsed, Kilobytes(parts[5]) * 1024);
    }

    /// <summary>An image line: the id, the moment as docker writes it — <c>2026-10-30 19:52:13 +0300 MSK</c> — and the size.</summary>
    private static ImageState? Image(string[] parts)
    {
        bool isMoment = DateTime.TryParseExact($"{parts[2]} {parts[3]}", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment);
        if (!isMoment || parts[4].Length != 5)
            return null;
        if (!int.TryParse(parts[4][1..3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours) || !int.TryParse(parts[4][3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes))
            return null;

        var offset = new TimeSpan(hours, minutes, 0) * (parts[4][0] == '-' ? -1 : 1);
        var createdAt = new DateTimeOffset(moment, TimeSpan.Zero) - offset;

        return new ImageState(parts[1], createdAt, parts[^1]);
    }

    private static long Kilobytes(string word) => long.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out long kilobytes) ? kilobytes : 0;

    /// <summary>A size as docker writes it — <c>5.931GB</c>, <c>228MB</c>, <c>0B</c> — in gigabytes.</summary>
    private static double Gigabytes(string size)
    {
        string digits = new([.. size.TakeWhile(letter => char.IsDigit(letter) || letter == '.')]);
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return 0;

        return size[digits.Length..].ToUpperInvariant() switch
        {
            "TB" => number * 1024,
            "GB" => number,
            "MB" => number / 1024,
            "KB" => number / 1024 / 1024,
            _ => 0
        };
    }

    /// <inheritdoc/>
    public async Task<Answer<bool>> RemoveAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken)
    {
        string workspace = RemoteShell.Under(host, key.Folder);
        string marker = RemoteShell.Under(host, $"{key.Folder}/{WorkspaceMarker.FileName}");
        var run = await RunScriptAsync(host, $"if [ -f {marker} ]; then rm -rf {workspace} && echo removed; else echo kept; fi", cancellationToken);

        if (!run.IsSuccess)
            return Answer.Failed<bool>(Messages.HostDown(host.Name, host.Address, Reason(run)));

        return Answer.Ok(run.Output.Contains("removed", StringComparison.Ordinal));
    }

    /// <summary>The script that provisions a host: the folders, the SDK, the workloads, the feeds, and a look at Xcode, Docker and sleep.</summary>
    internal static string ProvisionScript(Host host, BuilderManifest manifest)
    {
        string root = RemoteShell.Root(host);
        string folders = string.Join(' ', BuilderManifest.Folders.Select(folder => RemoteShell.Under(host, folder)));
        string sdk = RemoteShell.Quoted(manifest.SdkVersion);
        string workloads = RemoteShell.Quoted(string.Join(' ', manifest.Workloads));
        string feeds = RemoteShell.Quoted(string.Join(' ', manifest.Feeds));

        return $$$"""
            ROOT={{{root}}}
            mkdir -p {{{folders}}} && echo "s3l-step folders 1 $ROOT" || echo "s3l-step folders 0 could not create the folders under $ROOT"
            SDK={{{sdk}}}
            if [ -n "$SDK" ]; then
              have="$("$ROOT/dotnet/dotnet" --version 2>/dev/null)"
              if [ "$have" != "$SDK" ]; then
                curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$ROOT/bin/dotnet-install.sh" && bash "$ROOT/bin/dotnet-install.sh" --version "$SDK" --install-dir "$ROOT/dotnet" --no-path
                have="$("$ROOT/dotnet/dotnet" --version 2>/dev/null)"
              fi
              if [ "$have" = "$SDK" ]; then echo "s3l-step dotnet 1 $have in $ROOT/dotnet"; else echo "s3l-step dotnet 0 dotnet-install.sh did not leave $SDK in $ROOT/dotnet"; fi
              DOTNET="$ROOT/dotnet/dotnet"
            else
              have="$(dotnet --version 2>/dev/null)"
              if [ -n "$have" ]; then echo "s3l-step dotnet 1 $have, the host's own"; else echo "s3l-step dotnet 0 no dotnet on the host and none asked for: set SdkVersion"; fi
              DOTNET=dotnet
            fi
            WORKLOADS={{{workloads}}}
            if [ -n "$WORKLOADS" ] && [ -n "$have" ]; then
              missing=""
              for workload in $WORKLOADS; do "$DOTNET" workload list 2>/dev/null | grep -q "^$workload " || missing="$missing $workload"; done
              if [ -n "$missing" ]; then DOTNET_CLI_TELEMETRY_OPTOUT=1 "$DOTNET" workload install $missing; fi
              missing=""
              for workload in $WORKLOADS; do "$DOTNET" workload list 2>/dev/null | grep -q "^$workload " || missing="$missing $workload"; done
              if [ -z "$missing" ]; then echo "s3l-step workloads 1 $WORKLOADS"; else echo "s3l-step workloads 0 not installed:$missing"; fi
            fi
            FEEDS={{{feeds}}}
            for feed in $FEEDS; do
              mkdir -p "$ROOT/feeds/$feed"
              if "$DOTNET" nuget list source 2>/dev/null | grep -q "^ *[0-9]*\. *$feed "; then echo "s3l-step feed-$feed 1 $ROOT/feeds/$feed"; else "$DOTNET" nuget add source "$ROOT/feeds/$feed" --name "$feed" >/dev/null 2>&1 && echo "s3l-step feed-$feed 1 $ROOT/feeds/$feed" || echo "s3l-step feed-$feed 0 dotnet nuget add source failed"; fi
            done
            xcode="$(xcodebuild -version 2>/dev/null | head -n 1)"
            if [ -n "$xcode" ]; then echo "s3l-step xcode 1 $xcode"; else echo "s3l-step xcode 0 {{{Messages.InstallXcode}}}"; fi
            docker="$(docker version --format '{{.Server.Version}}' 2>/dev/null)"
            if [ -n "$docker" ]; then echo "s3l-step docker 1 Docker $docker"; else echo "s3l-step docker 0 {{{Messages.InstallDocker}}}"; fi
            if pmset -g 2>/dev/null | grep -q 'SleepDisabled *1'; then echo "s3l-step sleep 1 sleep is disabled"; else echo "s3l-step sleep 0 {{{Messages.KeepAwake}}}"; fi
            """;
    }

    /// <summary>The steps a provisioning log reports, in their order.</summary>
    internal static IReadOnlyList<ProvisionStep> Steps(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var reported = lines.Where(line => line.StartsWith("s3l-step ", StringComparison.Ordinal)).Select(line => line.Split(' ', 4));

        return [.. reported.Where(parts => parts.Length >= 3).Select(parts => new ProvisionStep(parts[1], parts[2] == "1", parts.ElementAtOrDefault(3)))];
    }

    private Task<ProgramRun> RunScriptAsync(Host host, string script, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var words = shell.Command(host, RemoteShell.Prelude(host) + script);

        return programs.RunAsync(words[0], words[1..], null, timeout ?? TimeSpan.FromSeconds(options.Value.ProbeSeconds), cancellationToken);
    }

    private async Task MarkAsync(Host host, WorkspaceMarker marker, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(marker, MarkerJson.Default.WorkspaceMarker);
        string file = RemoteShell.Under(host, $"{marker.Key.Folder}/{WorkspaceMarker.FileName}");
        string script = $"cat > {file} <<'S3L_MARKER'\n{json}\nS3L_MARKER\n";

        await RunScriptAsync(host, script, cancellationToken);
    }

    private async Task StopAsync(Host host, string pidFile)
    {
        string script = $"if [ -f {pidFile} ]; then pid=$(cat {pidFile}); pkill -P \"$pid\"; kill \"$pid\"; rm -f {pidFile}; fi";

        await RunScriptAsync(host, script, CancellationToken.None);
    }

    /// <summary>What a delivery leaves out, one rsync pattern a line: what a build makes again, a worktree's dead .git file, and what git ignores, anchored at the copy's root.</summary>
    private string Excludes(LocalCopy copy, IReadOnlyList<string> ignored)
    {
        var lines = new List<string>(options.Value.Excludes);
        if (copy.IsWorktree)
            lines.Add("/.git");
        lines.AddRange(ignored.Select(entry => $"/{entry}"));

        return string.Join('\n', lines) + '\n';
    }

    private IReadOnlyList<string> RsyncArguments(Host host, bool isDryRun, bool hasExcludes, string source, string destination)
    {
        var arguments = new List<string> { "-a", "--delete", "--itemize-changes", "--stats" };
        if (isDryRun)
            arguments.Add("--dry-run");
        if (hasExcludes)
            arguments.Add("--exclude-from=-");
        if (!host.IsLocal)
            arguments.AddRange(["-e", $"{options.Value.Ssh} -o BatchMode=yes -o ConnectTimeout=10"]);

        arguments.Add(source);
        arguments.Add(destination);

        return arguments;
    }

    private static string Reason(ProgramRun run) => run.IsTimedOut ? "it ran past its time" : string.IsNullOrWhiteSpace(run.Error) ? $"exit code {run.ExitCode}" : run.Error.Trim();

    private static Dictionary<string, string> Parsed(string output)
    {
        var pairs = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2);

        return pairs.ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }

    private static string? Version(Dictionary<string, string> answers, string key) =>
        answers.TryGetValue(key, out var version) && version.Length > 0 ? version : null;

    /// <summary>The files an itemized rsync run named: created, changed or deleted.</summary>
    /// <remarks>An item is its flags, a space and the path; openrsync writes nine flag characters where rsync 3 writes eleven, so the space is looked for rather than counted to.</remarks>
    private static IReadOnlyList<string> Changed(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var deleted = lines.Where(line => line.StartsWith("*deleting", StringComparison.Ordinal)).Select(line => line[9..].Trim());
        var items = lines.Select(line => (Flags: line.Split(' ', 2)[0], Path: line.Split(' ', 2).ElementAtOrDefault(1)));
        var written = items.Where(item => item.Path is not null && item.Flags.Length >= 9 && item.Flags[0] is '<' or '>' or 'c' && item.Flags[1] is 'f' or 'L').Select(item => item.Path!);

        return [.. deleted.Concat(written).Where(file => !file.EndsWith('/'))];
    }

    /// <summary>The bytes rsync's stats say went over the wire.</summary>
    private static long TransferredBytes(string output)
    {
        var line = output.Split('\n').FirstOrDefault(line => line.StartsWith("Total transferred file size:", StringComparison.Ordinal));
        var number = line?.Split(':', 2)[1].Trim().Split(' ')[0].Replace(",", string.Empty, StringComparison.Ordinal);

        return long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes) ? bytes : 0;
    }
}
