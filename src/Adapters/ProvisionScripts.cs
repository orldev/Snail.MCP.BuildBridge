using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>What provisioning runs on a host and how its answer is read: the script of the root, the SDK, the workloads, the feeds, the images and the tools, and the steps it reports.</summary>
public static class ProvisionScripts
{
    /// <summary>The script that provisions a host: the folders, the SDK, the workloads, the feeds, XcodeGen, the Android toolchain, the images, and a look at Xcode, Docker, Rosetta and sleep.</summary>
    /// <remarks>
    /// A second SDK goes beside the first with the host's unversioned files left alone: overwriting the dotnet muxer in
    /// place leaves macOS killing it, which the supply of an SDK during a run found on 2026-11-03. With several SDKs
    /// under the root, the one the settings name is looked for by its folder, not by what <c>dotnet --version</c> picks,
    /// which is the newest; and the workloads are listed and installed with that SDK, through a global.json beside it
    /// that the script runs in — found on 2026-11-06, when a run had put 10.0.403 beside 10.0.302.
    /// </remarks>
    public static string Of(Host host, BuilderManifest manifest)
    {
        string root = RemoteShell.Root(host);
        string folders = string.Join(' ', BuilderManifests.Folders.Select(folder => RemoteShell.Under(host, folder)));
        string sdk = RemoteShell.Quoted(manifest.SdkVersion);
        string workloads = RemoteShell.Quoted(string.Join(' ', manifest.Workloads));
        string feeds = RemoteShell.Quoted(string.Join(' ', manifest.Feeds));
        string images = string.Concat(manifest.Images.Select(image => ImageScript(image, manifest.SdkVersion)));
        string android = manifest.HasAndroid ? AndroidScript(manifest) : string.Empty;
        string node = manifest.HasNode ? NodeScript(manifest) : string.Empty;

        return $$$"""
            ROOT={{{root}}}
            mkdir -p {{{folders}}} && echo "s3l-step folders 1 $ROOT" || echo "s3l-step folders 0 could not create the folders under $ROOT"
            SDK={{{sdk}}}
            if [ -n "$SDK" ]; then
              have=""; [ -d "$ROOT/dotnet/sdk/$SDK" ] && have="$SDK"
              if [ "$have" != "$SDK" ]; then
                skip=""; [ -x "$ROOT/dotnet/dotnet" ] && skip=--skip-non-versioned-files
                curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$ROOT/bin/dotnet-install.sh" && bash "$ROOT/bin/dotnet-install.sh" --version "$SDK" --install-dir "$ROOT/dotnet" --no-path $skip
                [ -d "$ROOT/dotnet/sdk/$SDK" ] && have="$SDK"
              fi
              if [ "$have" = "$SDK" ]; then echo "s3l-step dotnet 1 $have in $ROOT/dotnet"; else echo "s3l-step dotnet 0 dotnet-install.sh did not leave $SDK in $ROOT/dotnet"; fi
              DOTNET="$ROOT/dotnet/dotnet"
              printf '{ "sdk": { "version": "%s" } }\n' "$SDK" > "$ROOT/dotnet/global.json" && cd "$ROOT/dotnet"
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
            {{{android}}}
            {{{node}}}
            cat > "$ROOT/{{{ContainerCommand.NuGetConfig}}}" <<'S3L_NUGET'
            {{{ContainerNuGetConfig(manifest.Feeds)}}}
            S3L_NUGET
            {{{images}}}
            """;
    }

    /// <summary>The lines that put the Android toolchain under the root: the JDK from its tarball into jdk, pointed at by jdk/current; the command-line tools from their zip into android; then the packages the manifest names through sdkmanager, once a person has accepted the licences.</summary>
    /// <remarks>
    /// Each archive is checked against the SHA-256 the manifest names before it is unpacked, and sdkmanager checks what it
    /// fetches itself. The JDK is fetched for Apple Silicon only, which every host of this server is; another host is
    /// told to put one in place by hand. Accepting Google's licences is a person's, as Rosetta's and Xcode's are, so the
    /// step stops there with the command and reports the packages installed once the licences are.
    /// </remarks>
    public static string AndroidScript(BuilderManifest manifest) =>
        $$$"""
        JDK={{{RemoteShell.Quoted(manifest.JdkVersion)}}}
        JDK_SHA={{{RemoteShell.Quoted(manifest.JdkSha256)}}}
        JDK_WANT="${JDK%%+*}"
        have="$("$ROOT/jdk/current/bin/java" -version 2>&1 | head -n 1 | sed 's/.*"\(.*\)".*/\1/')"
        reason="JDK $JDK did not install under $ROOT/jdk; the log says why"
        if [ "$have" != "$JDK_WANT" ] && [ "$(uname -m)" != arm64 ]; then reason="{{{Messages.JdkByHand}}}"
        elif [ "$have" != "$JDK_WANT" ]; then
          rm -rf "$ROOT/jdk"; mkdir -p "$ROOT/jdk"
          if curl -fsSL {{{RemoteShell.Quoted(BuilderManifests.JdkUrlOf(manifest.JdkVersion))}}} -o "$ROOT/jdk/jdk.tar.gz"; then
            if [ -z "$JDK_SHA" ] || echo "$JDK_SHA  $ROOT/jdk/jdk.tar.gz" | shasum -a 256 -c - >/dev/null 2>&1; then
              tar -xzf "$ROOT/jdk/jdk.tar.gz" -C "$ROOT/jdk"
              home="$(find "$ROOT/jdk" -type d -path '*/Contents/Home' | head -n 1)"
              [ -n "$home" ] && ln -sfn "$home" "$ROOT/jdk/current"
            else echo "jdk.tar.gz does not match the SHA-256 the settings name"; fi
            rm -f "$ROOT/jdk/jdk.tar.gz"
          fi
          have="$("$ROOT/jdk/current/bin/java" -version 2>&1 | head -n 1 | sed 's/.*"\(.*\)".*/\1/')"
        fi
        if [ "$have" = "$JDK_WANT" ]; then echo "s3l-step jdk 1 JDK $have in $ROOT/jdk"; else echo "s3l-step jdk 0 $reason"; fi
        TOOLS={{{RemoteShell.Quoted(manifest.AndroidToolsBuild)}}}
        TOOLS_SHA={{{RemoteShell.Quoted(manifest.AndroidToolsSha256)}}}
        PACKAGES={{{RemoteShell.Quoted(string.Join(' ', manifest.AndroidPackages))}}}
        SDKMANAGER="$ROOT/android/cmdline-tools/latest/bin/sdkmanager"
        export JAVA_HOME="$ROOT/jdk/current" ANDROID_HOME="$ROOT/android"
        if [ "$have" != "$JDK_WANT" ]; then echo "s3l-step android 0 no JDK under $ROOT/jdk, which sdkmanager runs on"
        else
          if [ ! -x "$SDKMANAGER" ]; then
            mkdir -p "$ROOT/android/cmdline-tools"
            if curl -fsSL {{{RemoteShell.Quoted(BuilderManifests.AndroidToolsUrlOf(manifest.AndroidToolsBuild))}}} -o "$ROOT/android/tools.zip"; then
              if [ -z "$TOOLS_SHA" ] || echo "$TOOLS_SHA  $ROOT/android/tools.zip" | shasum -a 256 -c - >/dev/null 2>&1; then
                rm -rf "$ROOT/android/cmdline-tools/latest" "$ROOT/android/cmdline-tools/cmdline-tools"
                unzip -qo "$ROOT/android/tools.zip" -d "$ROOT/android/cmdline-tools" && mv "$ROOT/android/cmdline-tools/cmdline-tools" "$ROOT/android/cmdline-tools/latest"
              else echo "the command-line tools zip does not match the SHA-256 the settings name"; fi
              rm -f "$ROOT/android/tools.zip"
            fi
          fi
          if [ ! -x "$SDKMANAGER" ]; then echo "s3l-step android 0 the Android command-line tools $TOOLS did not unpack under $ROOT/android; the log says why"
          elif [ ! -d "$ROOT/android/licenses" ]; then echo "s3l-step android 0 {{{Messages.AcceptAndroidLicences("$ROOT")}}}"
          else
            installed="$("$SDKMANAGER" --sdk_root="$ROOT/android" --list_installed 2>/dev/null)"
            missing=""
            for package in $PACKAGES; do printf '%s\n' "$installed" | grep -q "^ *$package " || missing="$missing $package"; done
            if [ -n "$missing" ]; then "$SDKMANAGER" --sdk_root="$ROOT/android" $missing </dev/null; fi
            installed="$("$SDKMANAGER" --sdk_root="$ROOT/android" --list_installed 2>/dev/null)"
            missing=""
            for package in $PACKAGES; do printf '%s\n' "$installed" | grep -q "^ *$package " || missing="$missing $package"; done
            if [ -z "$missing" ]; then echo "s3l-step android 1 $PACKAGES in $ROOT/android"; else echo "s3l-step android 0 not installed:$missing; the log says why"; fi
          fi
        fi
        """;

    /// <summary>The lines that put Node under the root: the release tarball into node, pointed at by node/current, checked against the SHA-256 the manifest names; the folder is removed and made anew, never written over.</summary>
    public static string NodeScript(BuilderManifest manifest) =>
        $$$"""
        NODE={{{RemoteShell.Quoted(manifest.NodeVersion)}}}
        NODE_SHA={{{RemoteShell.Quoted(manifest.NodeSha256)}}}
        have="$("$ROOT/node/current/bin/node" --version 2>/dev/null | sed 's/^v//')"
        reason="Node $NODE did not install under $ROOT/node; the log says why"
        if [ "$have" != "$NODE" ] && [ "$(uname -m)" != arm64 ]; then reason="{{{Messages.NodeByHand}}}"
        elif [ "$have" != "$NODE" ]; then
          rm -rf "$ROOT/node"; mkdir -p "$ROOT/node"
          if curl -fsSL {{{RemoteShell.Quoted(BuilderManifests.NodeUrlOf(manifest.NodeVersion))}}} -o "$ROOT/node/node.tar.gz"; then
            if [ -z "$NODE_SHA" ] || echo "$NODE_SHA  $ROOT/node/node.tar.gz" | shasum -a 256 -c - >/dev/null 2>&1; then
              tar -xzf "$ROOT/node/node.tar.gz" -C "$ROOT/node"
              home="$(find "$ROOT/node" -maxdepth 1 -type d -name 'node-v*' | head -n 1)"
              [ -n "$home" ] && ln -sfn "$home" "$ROOT/node/current"
            else echo "node.tar.gz does not match the SHA-256 the settings name"; fi
            rm -f "$ROOT/node/node.tar.gz"
          fi
          have="$("$ROOT/node/current/bin/node" --version 2>/dev/null | sed 's/^v//')"
        fi
        if [ "$have" = "$NODE" ]; then echo "s3l-step node 1 Node $have in $ROOT/node"; else echo "s3l-step node 0 $reason"; fi
        """;

    /// <summary>The lines that build one image: its Dockerfile written under the root, then docker build unless the tag is there already from this very Dockerfile.</summary>
    /// <remarks>
    /// An image of this server carries the label <see cref="ImageLabel"/>, not the owner label of what a run makes: a
    /// sweep of stale images must not take the toolchains with it. It also carries a hash of the Dockerfile it was built
    /// from, so a changed Dockerfile — a package added — is built again under the same tag, and an unchanged one is not.
    /// </remarks>
    public static string ImageScript(string image, string sdkVersion)
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
    public static string DockerfileHash(string dockerfile) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dockerfile.TrimEnd())))[..16];

    /// <summary>The NuGet configuration a container reads: nuget.org and every mirrored feed where the run mounts them.</summary>
    public static string ContainerNuGetConfig(IReadOnlyList<string> feeds)
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
    public static IReadOnlyList<ProvisionStep> Steps(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var reported = lines.Where(line => line.StartsWith("s3l-step ", StringComparison.Ordinal)).Select(line => line.Split(' ', 4));

        return [.. reported.Where(parts => parts.Length >= 3).Select(parts => new ProvisionStep(parts[1], parts[2] == "1", parts.ElementAtOrDefault(3)))];
    }
}
