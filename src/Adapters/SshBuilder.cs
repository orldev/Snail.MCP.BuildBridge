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
/// A delivery is the courier's — rsync once per run, or a Mutagen session — and the marker written beside the copy
/// afterwards is what proves a workspace this server's.
/// </para>
/// <para>
/// A run's command is started in the background of its script and its id written beside the workspace, so a run
/// stopped from this side — its ssh killed on a timeout — can be stopped on the host too, where killing the ssh
/// client alone would leave the build running.
/// </para>
/// </remarks>
public sealed class SshBuilder(IPrograms programs, ICourier courier, RemoteShell shell, IOptions<BuildOptions> options, TimeProvider clock) : IBuilder
{
    private const string PidFileName = ".s3l-build.pid";

    /// <inheritdoc/>
    public async Task<HostState> ProbeAsync(Host host, CancellationToken cancellationToken)
    {
        string script = $$$"""
            printf 'dotnet=%s\n' "$(dotnet --version 2>/dev/null)"
            printf 'sdks=%s\n' "$(ls {{{RemoteShell.Under(host, "dotnet/sdk")}}} 2>/dev/null | tr '\n' ' ')"
            printf 'xcode=%s\n' "$(xcodebuild -version 2>/dev/null | head -n 1 | sed 's/^Xcode //')"
            printf 'docker=%s\n' "$(docker version --format '{{.Server.Version}}' 2>/dev/null)"
            android=""; ndk="${ANDROID_NDK_HOME##*/}"
            if [ -n "$JAVA_HOME" ] && [ -x "$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager" ]; then android="jdk $(java -version 2>&1 | head -n 1 | sed 's/.*"\(.*\)".*/\1/'), ndk ${ndk:-none}"; fi
            printf 'android=%s\n' "$android"
            printf 'free=%s\n' "$(df -g "$HOME" | tail -n 1 | awk '{print $4}')"
            printf 'home=%s\n' "$HOME"
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
            new(Capability.Docker, Version(answers, "docker")),
            new(Capability.Android, Version(answers, "android"))
        };
        double? free = double.TryParse(answers.GetValueOrDefault("free"), NumberStyles.Float, CultureInfo.InvariantCulture, out double gigabytes) ? gigabytes : null;

        var sdks = answers.GetValueOrDefault("sdks", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new HostState(host, true, toolchains, free, null, Version(answers, "home")) { Sdks = sdks };
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
        var words = shell.Command(host, RemoteShell.Prelude(host) + ProvisionScripts.Of(host, manifest));
        var run = await programs.RunToLogAsync(words[0], words[1..], null, log, timeout, cancellationToken);

        if (run.IsMissing)
            return Answer.Failed<Provisioned>(Messages.ProgramMissing(words[0]));
        if (run.IsTimedOut)
            return Answer.Failed<Provisioned>(Messages.ProvisionFailed(host.Name, Messages.TimedOut(timeout)));

        var steps = ProvisionScripts.Steps(await File.ReadAllTextAsync(log, cancellationToken));
        if (steps.Count == 0)
            return Answer.Failed<Provisioned>(Messages.ProvisionFailed(host.Name, $"exit code {run.ExitCode}; see {log}"));

        return Answer.Ok(new Provisioned(host, steps, log));
    }

    /// <inheritdoc/>
    public async Task<Answer<Delivery>> DeliverAsync(Host host, LocalCopy copy, bool isDryRun, CancellationToken cancellationToken)
    {
        var delivered = await courier.DeliverAsync(host, copy, isDryRun, cancellationToken);
        if (delivered.Value is not { } delivery)
            return delivered;

        if (!isDryRun)
            await MarkAsync(host, new WorkspaceMarker(copy.Workspace, $"{Environment.MachineName}:{copy.Path}", copy.Commit, clock.GetUtcNow(), null), cancellationToken);

        return Answer.Ok(delivery);
    }

    /// <inheritdoc/>
    public async Task<Answer<Delivery>> MirrorAsync(Host host, string source, string relative, CancellationToken cancellationToken)
    {
        var key = new WorkspaceKey(relative, string.Empty);
        var made = await RunScriptAsync(host, $"mkdir -p {RemoteShell.Under(host, relative)}", cancellationToken);
        if (!made.IsSuccess)
            return Answer.Failed<Delivery>(Messages.DeliveryFailed(host.Name, Rsync.Reason(made)));

        var started = clock.GetTimestamp();
        var arguments = Rsync.Arguments(options.Value, host, false, false, $"{source.TrimEnd('/')}/", $"{RemoteShell.Rsync(host, relative)}/");
        var run = await programs.RunAsync(options.Value.Rsync, arguments, null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken);
        if (run.IsMissing)
            return Answer.Failed<Delivery>(Messages.ProgramMissing(options.Value.Rsync));
        if (!run.IsSuccess)
            return Answer.Failed<Delivery>(Messages.DeliveryFailed(host.Name, Rsync.Reason(run)));

        return Answer.Ok(new Delivery(key, Rsync.Changed(run.Output), Rsync.TransferredBytes(run.Output), clock.GetElapsedTime(started), false));
    }

    /// <inheritdoc/>
    public async Task<Answer<Execution>> ExecuteAsync(Host host, WorkspaceKey key, IReadOnlyList<string> command, string log, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string workspace = RemoteShell.Under(host, key.CopyFolder);
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
            return Answer.Failed<string>(Messages.SimulatorFailed(device, Rsync.Reason(run)), Messages.SimulatorHint);

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

            var arguments = Rsync.Arguments(options.Value, host, false, false, RemoteShell.Rsync(host, $"{key.CopyFolder}/{path}"), $"{into}/");
            var run = await programs.RunAsync(options.Value.Rsync, arguments, null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken);
            if (run.IsMissing)
                return Answer.Failed<Collected>(Messages.ProgramMissing(options.Value.Rsync));
            if (!run.IsSuccess)
                return Answer.Failed<Collected>(Messages.CollectionFailed(host.Name, Rsync.Reason(run)));

            bytes += Rsync.TransferredBytes(run.Output);
        }

        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file))
            .Order(StringComparer.Ordinal)
            .ToList();

        return Answer.Ok(new Collected(folder, files, bytes));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every installer is asked only for what is not there, and reads no terminal, so a licence not yet accepted fails
    /// sdkmanager instead of waiting on a prompt; the answer then carries the command a person runs, the same
    /// provisioning names. sdkmanager draws its progress with carriage returns and ends without a line break, so the
    /// script's own lines are read between either kind of break. Google now names a platform with a minor version —
    /// <c>platforms;android-37.0</c> — where a build names its API level, so a package not on offer as named is taken
    /// as the first on offer with a minor version after it. A workload install runs in the copy, so dotnet takes the
    /// SDK its global.json names. A second SDK goes beside the first without the host's unversioned files: dotnet-install
    /// would overwrite the dotnet muxer in place, and macOS kills a program whose file changed under its cached code
    /// signature — every dotnet on the host answered Killed: 9 after the first SDK supplied on 2026-11-03, until the
    /// muxer was given a fresh inode.
    /// </remarks>
    public async Task<Answer<IReadOnlyList<string>>> SupplyAsync(Host host, Supply supply, CancellationToken cancellationToken)
    {
        string script = supply.Kind switch
        {
            SupplyKind.AndroidPackages => PackagesScript(host, supply.Names),
            SupplyKind.Sdk => SdkScript(host, supply.Names[0]),
            SupplyKind.Workloads => WorkloadsScript(host, supply),
            _ => throw new ArgumentOutOfRangeException(nameof(supply), supply.Kind, null)
        };

        var run = await RunScriptAsync(host, script, cancellationToken, TimeSpan.FromSeconds(options.Value.RunSeconds));
        if (!run.IsSuccess)
            return Answer.Failed<IReadOnlyList<string>>(Messages.SupplyFailed(host.Name, supply.Words, Rsync.Reason(run)));

        var lines = run.Output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string root = lines.FirstOrDefault(line => line.StartsWith("s3l-root ", StringComparison.Ordinal))?[9..] ?? host.Root;
        var said = lines.LastOrDefault(line => line.StartsWith("s3l-supply ", StringComparison.Ordinal))?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

        return said switch
        {
            ["s3l-supply", "ok", .. var installed] => Answer.Ok((supply with { Names = installed }).Words),
            ["s3l-supply", "none"] => Answer.Failed<IReadOnlyList<string>>(Messages.HostCannot(host.Name, supply.Kind == SupplyKind.AndroidPackages ? "android" : "dotnet"), Messages.ProvisionFirst),
            ["s3l-supply", "left", .. var left] => Answer.Failed<IReadOnlyList<string>>(Messages.SupplyFailed(host.Name, (supply with { Names = left }).Words, LastLineOf(run.Error)), supply.Kind == SupplyKind.AndroidPackages ? Messages.SupplyHint(root) : null),
            _ => Answer.Failed<IReadOnlyList<string>>(Messages.SupplyFailed(host.Name, supply.Words, "the installer said nothing this server reads"))
        };
    }

    private static string PackagesScript(Host host, IReadOnlyList<string> packages) =>
        $$$"""
        ROOT={{{RemoteShell.Root(host)}}}
        SDKMANAGER="$ROOT/android/cmdline-tools/latest/bin/sdkmanager"
        echo "s3l-root $ROOT"
        [ -x "$SDKMANAGER" ] && [ -x "$ROOT/jdk/current/bin/java" ] || { echo "s3l-supply none"; exit 0; }
        PACKAGES={{{RemoteShell.Quoted(string.Join(' ', packages))}}}
        installed="$("$SDKMANAGER" --sdk_root="$ROOT/android" --list_installed 2>/dev/null)"
        missing=""
        for package in $PACKAGES; do printf '%s\n' "$installed" | grep -q "^ *$package " || missing="$missing $package"; done
        todo=""; left=""
        if [ -n "$missing" ]; then
          available="$("$SDKMANAGER" --sdk_root="$ROOT/android" --list 2>/dev/null)"
          for package in $missing; do
            if printf '%s\n' "$available" | grep -q "^ *$package "; then wanted="$package"
            else wanted="$(printf '%s\n' "$available" | grep -o "^ *$package\.[0-9][0-9]* " | head -n 1 | tr -d ' ')"; fi
            printf '%s\n' "$installed" | grep -q "^ *${wanted:-$package} " || todo="$todo ${wanted:-$package}"
          done
          [ -n "$todo" ] && "$SDKMANAGER" --sdk_root="$ROOT/android" $todo </dev/null
          installed="$("$SDKMANAGER" --sdk_root="$ROOT/android" --list_installed 2>/dev/null)"
          for package in $todo; do printf '%s\n' "$installed" | grep -q "^ *$package " || left="$left $package"; done
        fi
        if [ -z "$left" ]; then echo "s3l-supply ok$todo"; else echo "s3l-supply left$left"; fi
        """;

    private static string SdkScript(Host host, string version) =>
        $$$"""
        ROOT={{{RemoteShell.Root(host)}}}
        VERSION={{{RemoteShell.Quoted(version)}}}
        echo "s3l-root $ROOT"
        if [ -d "$ROOT/dotnet/sdk/$VERSION" ]; then echo "s3l-supply ok"; exit 0; fi
        mkdir -p "$ROOT/bin" "$ROOT/dotnet"
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$ROOT/bin/dotnet-install.sh" && bash "$ROOT/bin/dotnet-install.sh" --version "$VERSION" --install-dir "$ROOT/dotnet" --no-path --skip-non-versioned-files </dev/null
        if [ -d "$ROOT/dotnet/sdk/$VERSION" ]; then echo "s3l-supply ok $VERSION"; else echo "s3l-supply left $VERSION"; fi
        """;

    private static string WorkloadsScript(Host host, Supply supply) =>
        $$$"""
        ROOT={{{RemoteShell.Root(host)}}}
        IDS={{{RemoteShell.Quoted(string.Join(' ', supply.Names))}}}
        echo "s3l-root $ROOT"
        command -v dotnet >/dev/null 2>&1 || { echo "s3l-supply none"; exit 0; }
        {{{(supply.Folder is { } folder ? $"cd {RemoteShell.Under(host, folder)} || exit 97" : string.Empty)}}}
        missing=""
        for id in $IDS; do dotnet workload list 2>/dev/null | grep -q "^$id " || missing="$missing $id"; done
        [ -n "$missing" ] && dotnet workload install $missing </dev/null
        left=""
        for id in $missing; do dotnet workload list 2>/dev/null | grep -q "^$id " || left="$left $id"; done
        if [ -z "$left" ]; then echo "s3l-supply ok$missing"; else echo "s3l-supply left$left"; fi
        """;

    /// <inheritdoc/>
    public async Task<Answer<Survey>> SurveyAsync(Host host, CancellationToken cancellationToken)
    {
        var run = await RunScriptAsync(host, Surveys.Script(host), cancellationToken, TimeSpan.FromSeconds(options.Value.TransferSeconds));
        if (!run.IsSuccess)
            return Answer.Failed<Survey>(Messages.SurveyFailed(host.Name, Rsync.Reason(run)));

        return Answer.Ok(Surveys.Of(host, run.Output));
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
            SweepKind.Images when item.Name == "all" => $"docker image prune -a --force --filter label={DockerCommand.Label} >/dev/null && (docker image ls --filter label={ProvisionScripts.ImageLabel} -q | xargs docker image rm -f >/dev/null 2>&1; echo removed)",
            SweepKind.Images when item.Name.Contains(':') && AndroidPackages.IsName(item.Name.Replace(':', ';')) => $"docker image rm -f {RemoteShell.Quoted(item.Name)} >/dev/null && echo removed",
            SweepKind.Sdk when SdkWish.IsVersion(item.Name) => $"rm -rf {RemoteShell.Under(host, $"dotnet/sdk/{item.Name}")} && echo removed",
            SweepKind.Package when AndroidPackages.IsName(item.Name) => $"{RemoteShell.Under(host, "android/cmdline-tools/latest/bin/sdkmanager")} --sdk_root={RemoteShell.Under(host, "android")} --uninstall {RemoteShell.Quoted(item.Name)} </dev/null >/dev/null 2>&1 && echo removed",
            SweepKind.Images => $"docker image prune -a --force --filter label={DockerCommand.Label} --filter until={item.Name} >/dev/null && echo removed",
            SweepKind.BuildCache => $"docker builder prune -a --force {(item.Name == "all" ? string.Empty : $"--filter until={item.Name}")} >/dev/null && echo removed",
            SweepKind.Packages => $"rm -rf {RemoteShell.Under(host, "nuget")} {RemoteShell.Under(host, "gradle")} && echo removed",
            _ => string.Empty
        };
        if (script.Length == 0)
            return Answer.Ok(false);

        var run = await RunScriptAsync(host, script, cancellationToken, TimeSpan.FromMinutes(30));
        if (!run.IsSuccess)
            return Answer.Failed<bool>(Rsync.Reason(run));

        return Answer.Ok(run.Output.Contains("removed", StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public async Task<Answer<bool>> RemoveAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken)
    {
        await courier.ForgetAsync(host, key, cancellationToken);

        string workspace = RemoteShell.Under(host, key.Folder);
        string marker = RemoteShell.Under(host, $"{key.Folder}/{WorkspaceMarker.FileName}");
        var run = await RunScriptAsync(host, $"if [ -f {marker} ]; then rm -rf {workspace} && echo removed; else echo kept; fi", cancellationToken);

        if (!run.IsSuccess)
            return Answer.Failed<bool>(Messages.HostDown(host.Name, host.Address, Rsync.Reason(run)));

        return Answer.Ok(run.Output.Contains("removed", StringComparison.Ordinal));
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

    private static string LastLineOf(string text) =>
        text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "sdkmanager said nothing";

    private static Dictionary<string, string> Parsed(string output)
    {
        var pairs = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2);

        return pairs.ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }

    private static string? Version(Dictionary<string, string> answers, string key) =>
        answers.TryGetValue(key, out var version) && version.Length > 0 ? version : null;
}
