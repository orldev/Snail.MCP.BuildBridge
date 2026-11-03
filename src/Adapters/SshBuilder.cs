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
        const string script = """
            printf 'dotnet=%s\n' "$(dotnet --version 2>/dev/null)"
            printf 'xcode=%s\n' "$(xcodebuild -version 2>/dev/null | head -n 1 | sed 's/^Xcode //')"
            printf 'docker=%s\n' "$(docker version --format '{{.Server.Version}}' 2>/dev/null)"
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
            new(Capability.Docker, Version(answers, "docker"))
        };
        double? free = double.TryParse(answers.GetValueOrDefault("free"), NumberStyles.Float, CultureInfo.InvariantCulture, out double gigabytes) ? gigabytes : null;

        return new HostState(host, true, toolchains, free, null, Version(answers, "home"));
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

            var arguments = Rsync.Arguments(options.Value, host, false, false, RemoteShell.Rsync(host, $"{key.Folder}/{path}"), $"{into}/");
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
    public async Task<Answer<Survey>> SurveyAsync(Host host, CancellationToken cancellationToken)
    {
        var run = await RunScriptAsync(host, SurveyScript(host), cancellationToken, TimeSpan.FromSeconds(options.Value.TransferSeconds));
        if (!run.IsSuccess)
            return Answer.Failed<Survey>(Messages.SurveyFailed(host.Name, Rsync.Reason(run)));

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
            SweepKind.Images when item.Name == "all" => $"docker image prune -a --force --filter label={DockerCommand.Label} >/dev/null && (docker image ls --filter label={ImageLabel} -q | xargs docker image rm -f >/dev/null 2>&1; echo removed)",
            SweepKind.Images => $"docker image prune -a --force --filter label={DockerCommand.Label} --filter until={item.Name} >/dev/null && echo removed",
            SweepKind.BuildCache => $"docker builder prune -a --force {(item.Name == "all" ? string.Empty : $"--filter until={item.Name}")} >/dev/null && echo removed",
            SweepKind.Packages => $"rm -rf {RemoteShell.Under(host, "nuget")} && echo removed",
            _ => string.Empty
        };
        if (script.Length == 0)
            return Answer.Ok(false);

        var run = await RunScriptAsync(host, script, cancellationToken, TimeSpan.FromMinutes(30));
        if (!run.IsSuccess)
            return Answer.Failed<bool>(Rsync.Reason(run));

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
        await courier.ForgetAsync(host, key, cancellationToken);

        string workspace = RemoteShell.Under(host, key.Folder);
        string marker = RemoteShell.Under(host, $"{key.Folder}/{WorkspaceMarker.FileName}");
        var run = await RunScriptAsync(host, $"if [ -f {marker} ]; then rm -rf {workspace} && echo removed; else echo kept; fi", cancellationToken);

        if (!run.IsSuccess)
            return Answer.Failed<bool>(Messages.HostDown(host.Name, host.Address, Rsync.Reason(run)));

        return Answer.Ok(run.Output.Contains("removed", StringComparison.Ordinal));
    }

    /// <summary>The script that provisions a host: the folders, the SDK, the workloads, the feeds, and a look at Xcode, Docker and sleep.</summary>
    internal static string ProvisionScript(Host host, BuilderManifest manifest)
    {
        string root = RemoteShell.Root(host);
        string folders = string.Join(' ', BuilderManifests.Folders.Select(folder => RemoteShell.Under(host, folder)));
        string sdk = RemoteShell.Quoted(manifest.SdkVersion);
        string workloads = RemoteShell.Quoted(string.Join(' ', manifest.Workloads));
        string feeds = RemoteShell.Quoted(string.Join(' ', manifest.Feeds));
        string images = string.Concat(manifest.Images.Select(image => ImageScript(image, manifest.SdkVersion)));

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
            if [ -n "$xcode" ] && [ {{{(manifest.HasXcodeGen ? 1 : 0)}}} = 1 ]; then
              XCODEGEN={{{RemoteShell.Quoted(manifest.XcodeGenVersion)}}}
              XCODEGEN_SHA={{{RemoteShell.Quoted(manifest.XcodeGenSha256)}}}
              have="$("$ROOT/bin/xcodegen" --version 2>/dev/null | sed 's/^Version: //')"
              if [ "$have" != "$XCODEGEN" ]; then
                rm -rf "$ROOT/xcodegen" "$ROOT/bin/xcodegen"; mkdir -p "$ROOT/xcodegen"
                if curl -fsSL {{{RemoteShell.Quoted(BuilderManifests.XcodeGenUrlOf(manifest.XcodeGenVersion))}}} -o "$ROOT/xcodegen/xcodegen.zip"; then
                  if [ -z "$XCODEGEN_SHA" ] || echo "$XCODEGEN_SHA  $ROOT/xcodegen/xcodegen.zip" | shasum -a 256 -c - >/dev/null 2>&1; then
                    unzip -qo "$ROOT/xcodegen/xcodegen.zip" -d "$ROOT/xcodegen"
                    real="$(find "$ROOT/xcodegen" -type f -path '*/bin/xcodegen' | head -n 1)"
                    if [ -n "$real" ]; then chmod +x "$real"; printf '#!/bin/sh\nexec "%s" "$@"\n' "$real" > "$ROOT/bin/xcodegen"; chmod +x "$ROOT/bin/xcodegen"; fi
                  else echo "xcodegen.zip does not match the SHA-256 the settings name"; fi
                  rm -f "$ROOT/xcodegen/xcodegen.zip"
                fi
                have="$("$ROOT/bin/xcodegen" --version 2>/dev/null | sed 's/^Version: //')"
              fi
              if [ "$have" = "$XCODEGEN" ]; then echo "s3l-step xcodegen 1 $have in $ROOT/xcodegen"; else echo "s3l-step xcodegen 0 xcodegen $XCODEGEN did not install under $ROOT/xcodegen; the log says why"; fi
            fi
            docker="$(docker version --format '{{.Server.Version}}' 2>/dev/null)"
            if [ -n "$docker" ]; then echo "s3l-step docker 1 Docker $docker"; else echo "s3l-step docker 0 {{{Messages.InstallDocker}}}"; fi
            if pmset -g 2>/dev/null | grep -Eq '^ *(SleepDisabled +1|sleep +0)'; then echo "s3l-step sleep 1 the host does not sleep"; else echo "s3l-step sleep 0 {{{Messages.KeepAwake}}}"; fi
            if [ "$(uname -m)" = arm64 ]; then
              if arch -x86_64 /usr/bin/true >/dev/null 2>&1; then echo "s3l-step rosetta 1 x64 programs run"; else echo "s3l-step rosetta 0 {{{Messages.InstallRosetta}}}"; fi
            fi
            cat > "$ROOT/{{{ContainerCommand.NuGetConfig}}}" <<'S3L_NUGET'
            {{{ContainerNuGetConfig(manifest.Feeds)}}}
            S3L_NUGET
            {{{images}}}
            """;
    }

    /// <summary>The lines that build one image: its Dockerfile written under the root, then docker build unless the tag is there already from this very Dockerfile.</summary>
    /// <remarks>
    /// An image of this server carries the label <see cref="ImageLabel"/>, not the owner label of what a run makes: a
    /// sweep of stale images must not take the toolchains with it. It also carries a hash of the Dockerfile it was built
    /// from, so a changed Dockerfile — a package added — is built again under the same tag, and an unchanged one is not.
    /// </remarks>
    internal static string ImageScript(string image, string sdkVersion)
    {
        string dockerfile = BuildImages.DockerfileOf(image) ?? string.Empty;
        string tag = BuildImage.TagOf(image, sdkVersion);
        string platform = BuildImage.PlatformOf(image) is { } wanted ? $"--platform {wanted} " : string.Empty;
        string sdk = sdkVersion.Length > 0 ? sdkVersion : "10.0";
        string hash = DockerfileHash(dockerfile);
        if (dockerfile.Length == 0)
            return $"echo \"s3l-step image-{image} 0 this server carries no Dockerfile named {image}\"\n";

        return $$$"""
            mkdir -p "$ROOT/images/{{{image}}}"
            cat > "$ROOT/images/{{{image}}}/Dockerfile" <<'S3L_DOCKERFILE'
            {{{dockerfile.TrimEnd()}}}
            S3L_DOCKERFILE
            if [ -z "$docker" ]; then echo "s3l-step image-{{{image}}} 0 docker is not there to build {{{tag}}}"
            elif [ "$(docker image inspect --format '{{index .Config.Labels "{{{DockerfileLabel}}}"}}' {{{RemoteShell.Quoted(tag)}}} 2>/dev/null)" = "{{{hash}}}" ]; then echo "s3l-step image-{{{image}}} 1 {{{tag}}}"
            elif docker build {{{platform}}}--label {{{ImageLabel}}} --label {{{DockerfileLabel}}}={{{hash}}} --build-arg SDK={{{RemoteShell.Quoted(sdk)}}} -t {{{RemoteShell.Quoted(tag)}}} "$ROOT/images/{{{image}}}"; then echo "s3l-step image-{{{image}}} 1 {{{tag}}} built"
            else echo "s3l-step image-{{{image}}} 0 docker build of {{{tag}}} failed; the log says why"; fi

            """;
    }

    /// <summary>The label an image of this server carries.</summary>
    public const string ImageLabel = "snail.image=buildbridge";

    /// <summary>The label carrying the hash of the Dockerfile an image was built from.</summary>
    public const string DockerfileLabel = "snail.dockerfile";

    /// <summary>The hash of a Dockerfile as its image's label carries it: sixteen hex characters of SHA-256, enough to tell one text from another.</summary>
    internal static string DockerfileHash(string dockerfile) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dockerfile.TrimEnd())))[..16];

    /// <summary>The NuGet configuration a container reads: nuget.org and every mirrored feed where the run mounts them.</summary>
    internal static string ContainerNuGetConfig(IReadOnlyList<string> feeds)
    {
        var sources = feeds.Select(feed => $"""    <add key="{feed}" value="{ContainerCommand.Feeds}/{feed}" />""");

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
            {string.Join('\n', sources)}
              </packageSources>
            </configuration>
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
