using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>How a shell script reaches a host: through ssh, or straight into a shell when the host is this machine.</summary>
/// <remarks>
/// A script is handed to ssh as one argument, quoted for the host's login shell, which hands it on to <c>/bin/sh</c>:
/// the login shell of an account is zsh on a Mac, and zsh leaves <c>$words</c> as one word where sh splits them, so
/// a script written for sh runs in sh on every host. A non-interactive shell reads no profile, so the script itself
/// puts the root's programs on the path. The root is spoken of as <c>$HOME</c> rather than <c>~</c>, which only an
/// interactive shell expands inside quotes.
/// <para>
/// Every command to a host goes over one connection, which ssh opens with the first and keeps for a while after the
/// last: a Mac's sshd is started by launchd for each connection and takes forty-two handshakes at a time, so a server
/// that connected anew for every step was turned away whenever many sessions reconnected at once (2026-11-12). Over
/// the shared connection a command takes 0.4 s where a connection of its own took 1.6 s, and past the ten sessions
/// sshd gives one connection ssh opens another by itself — fourteen commands at once all ended well.
/// </para>
/// </remarks>
public sealed class RemoteShell(IOptions<BuildOptions> options)
{
    /// <summary>Where ssh keeps the socket of a shared connection: the account's ssh folder, named by ssh's own hash of the host, the port and the user.</summary>
    public const string SharedSocket = "~/.ssh/s3l-%C";

    /// <summary>The ssh program with the options every connection of this server is made with: no questions asked, ten seconds to connect, a link found dead within a minute, and one connection shared for as long as the settings say.</summary>
    public IReadOnlyList<string> Ssh { get; } = SshOf(options.Value, SharedSocket);

    /// <summary>The same in a line, as rsync takes its remote shell.</summary>
    public string SshLine => string.Join(' ', Ssh);

    /// <summary>How many connections builds share among themselves, apart from the one short commands share.</summary>
    public const int Lanes = 3;

    /// <summary>The program and arguments that run <paramref name="script"/> on <paramref name="host"/>: the program first.</summary>
    public string[] Command(Host host, string script) =>
        host.IsLocal
            ? ["/bin/sh", "-c", script]
            : [.. Ssh, host.Address, $"exec /bin/sh -c {Quoted(script)}"];

    /// <summary>The same for a script that runs long — a build, provisioning — over the connection builds of <paramref name="turn"/> share, not the one short commands do.</summary>
    /// <remarks>
    /// sshd gives one connection ten sessions, and a build holds its session for as long as it runs. Builds over the
    /// connection short commands share held them all, so with ten builds going every short command opened a connection
    /// of its own, and ssh's two lines about it — <c>mux_client_request_session</c>, <c>disabling multiplexing</c> —
    /// went into the build's log (fourteen commands at once, 2026-11-12). A connection of its own for every build was
    /// 1.2 s more a run for the handshake. So builds share three connections of theirs, a workspace always the same
    /// one: thirty builds at once before one opens its own.
    /// </remarks>
    public string[] LongCommand(Host host, string script, string turn) =>
        host.IsLocal
            ? ["/bin/sh", "-c", script]
            : [.. SshOf(options.Value, $"{SharedSocket}-{LaneOf(turn)}"), host.Address, $"exec /bin/sh -c {Quoted(script)}"];

    /// <summary>The lane a name always gets, from 0 up to <see cref="Lanes"/>.</summary>
    internal static int LaneOf(string turn) => (int)(turn.Aggregate(0u, (sum, letter) => (sum * 31) + letter) % Lanes);

    private static List<string> SshOf(BuildOptions options, string socket)
    {
        List<string> words = [options.Ssh, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=15", "-o", "ServerAliveCountMax=4"];
        if (options.SshShareMinutes > 0)
            words.AddRange(["-o", "ControlMaster=auto", "-o", $"ControlPath={socket}", "-o", $"ControlPersist={options.SshShareMinutes * 60}"]);

        return words;
    }

    /// <summary>
    /// The lines every script starts with: the root's programs first on the path, then the places a Mac keeps programs a
    /// person installed — OrbStack's docker, Homebrew — which a non-interactive shell does not look in, the root's SDK and
    /// packages for dotnet, dotnet's words in English, and the Android toolchain under the root where it is: the JDK,
    /// the SDK with its tools, the newest NDK and Gradle's own folder, named the way gradle and dotnet look for them, and
    /// Node with npm's cache and Playwright's browsers under the root too.
    /// </summary>
    public static string Prelude(Host host) =>
        $"""
        export PATH={Root(host)}/bin:{Root(host)}/dotnet:/usr/local/bin:/opt/homebrew/bin:"$HOME/.orbstack/bin":"$PATH"
        export NUGET_PACKAGES={Under(host, "nuget")} DOTNET_CLI_UI_LANGUAGE=en DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
        [ -d {Under(host, "dotnet")} ] && export DOTNET_ROOT={Under(host, "dotnet")}
        [ -x {Under(host, "jdk/current/bin/java")} ] && export JAVA_HOME={Under(host, "jdk/current")} PATH={Under(host, "jdk/current/bin")}:"$PATH"
        [ -x {Under(host, "android/cmdline-tools/latest/bin/sdkmanager")} ] && export ANDROID_HOME={Under(host, "android")} ANDROID_SDK_ROOT={Under(host, "android")} GRADLE_USER_HOME={Under(host, "gradle")} PATH={Under(host, "android/cmdline-tools/latest/bin")}:{Under(host, "android/platform-tools")}:"$PATH"
        S3L_NDK="$(ls {Under(host, "android/ndk")} 2>/dev/null | sort -V | tail -n 1)"; [ -n "$S3L_NDK" ] && export ANDROID_NDK_HOME={Under(host, "android/ndk")}/"$S3L_NDK"
        [ -x {Under(host, "node/current/bin/node")} ] && export PATH={Under(host, "node/current/bin")}:"$PATH" npm_config_cache={Under(host, "npm")} PLAYWRIGHT_BROWSERS_PATH={Under(host, "playwright")}

        """;

    /// <summary>The host's root as a shell expression, double-quoted, with the home folder spelled out.</summary>
    public static string Root(Host host) =>
        host.Root.StartsWith("~/", StringComparison.Ordinal)
            ? $"\"$HOME/{host.Root[2..]}\""
            : Quoted(host.Root);

    /// <summary>A folder under the host's root as a shell expression, quoted as the root is.</summary>
    public static string Under(Host host, string relative) =>
        host.Root.StartsWith("~/", StringComparison.Ordinal)
            ? $"\"$HOME/{host.Root[2..]}/{relative}\""
            : Quoted($"{host.Root}/{relative}");

    /// <summary>The host's root as this machine's file system sees it, when the host is this machine.</summary>
    public static string LocalRoot(Host host) => ServerFolders.ExpandHome(host.Root);

    /// <summary>A folder under the host's root as rsync names it: a local path on this machine, <c>address:path</c> on another.</summary>
    public static string Rsync(Host host, string relative) =>
        host.IsLocal
            ? Path.Combine(LocalRoot(host), relative)
            : $"{host.Address}:{host.Root}/{relative}";

    /// <summary>The lines a POSIX shell reads to take <paramref name="variables"/> into its environment, each value quoted for it; they go to the shell on its standard input, never on its command line.</summary>
    public static string Exports(IReadOnlyDictionary<string, string> variables) =>
        string.Concat(variables.Select(variable => $"export {variable.Key}={Quoted(variable.Value)}\n"));

    /// <summary>A word quoted for a POSIX shell.</summary>
    public static string Quoted(string word) => $"'{word.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    /// <summary>A command's words quoted for a POSIX shell, joined by spaces.</summary>
    public static string Quoted(IEnumerable<string> words) => string.Join(' ', words.Select(Quoted));
}
