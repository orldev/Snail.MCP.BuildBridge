using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>How a shell script reaches a host: through ssh, or straight into a shell when the host is this machine.</summary>
/// <remarks>
/// A script is handed to ssh as one argument, which the host's login shell runs as it is, newlines and quotes
/// included; a non-interactive shell reads no profile, so the script itself puts the root's programs on the path. The
/// root is spoken of as <c>$HOME</c> rather than <c>~</c>, which only an interactive shell expands inside quotes.
/// </remarks>
public sealed class RemoteShell(IOptions<BuildOptions> options)
{
    /// <summary>The program and arguments that run <paramref name="script"/> on <paramref name="host"/>: the program first.</summary>
    public string[] Command(Host host, string script) =>
        host.IsLocal
            ? ["/bin/sh", "-c", script]
            : [options.Value.Ssh, "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", host.Address, script];

    /// <summary>The lines every script starts with: the root's programs first on the path, the root's SDK and packages for dotnet, and dotnet's words in English.</summary>
    public static string Prelude(Host host) =>
        $"""
        export PATH={Root(host)}/bin:{Root(host)}/dotnet:"$PATH"
        export NUGET_PACKAGES={Under(host, "nuget")} DOTNET_CLI_UI_LANGUAGE=en DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
        [ -d {Under(host, "dotnet")} ] && export DOTNET_ROOT={Under(host, "dotnet")}

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
    public static string LocalRoot(Host host) =>
        host.Root.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), host.Root[2..])
            : host.Root;

    /// <summary>A folder under the host's root as rsync names it: a local path on this machine, <c>address:path</c> on another.</summary>
    public static string Rsync(Host host, string relative) =>
        host.IsLocal
            ? Path.Combine(LocalRoot(host), relative)
            : $"{host.Address}:{host.Root}/{relative}";

    /// <summary>A word quoted for a POSIX shell.</summary>
    public static string Quoted(string word) => $"'{word.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    /// <summary>A command's words quoted for a POSIX shell, joined by spaces.</summary>
    public static string Quoted(IEnumerable<string> words) => string.Join(' ', words.Select(Quoted));
}
