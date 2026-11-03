using System.Globalization;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>How rsync is called and read: the arguments of a mirror, and the files and bytes its output names.</summary>
internal static class Rsync
{
    /// <summary>The arguments of a mirror from <paramref name="source"/> to <paramref name="destination"/>, over ssh unless the host is this machine, with excludes on the standard input when asked.</summary>
    public static IReadOnlyList<string> Arguments(BuildOptions options, Host host, bool isDryRun, bool hasExcludes, string source, string destination)
    {
        var arguments = new List<string> { "-a", "--delete", "--itemize-changes", "--stats" };
        if (isDryRun)
            arguments.Add("--dry-run");
        if (hasExcludes)
            arguments.Add("--exclude-from=-");
        if (!host.IsLocal)
            arguments.AddRange(["-e", $"{options.Ssh} -o BatchMode=yes -o ConnectTimeout=10"]);

        arguments.Add(source);
        arguments.Add(destination);

        return arguments;
    }

    /// <summary>The files an itemized rsync run named: created, changed or deleted.</summary>
    /// <remarks>An item is its flags, a space and the path; openrsync writes nine flag characters where rsync 3 writes eleven, so the space is looked for rather than counted to.</remarks>
    public static IReadOnlyList<string> Changed(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var deleted = lines.Where(line => line.StartsWith("*deleting", StringComparison.Ordinal)).Select(line => line[9..].Trim());
        var items = lines.Select(line => (Flags: line.Split(' ', 2)[0], Path: line.Split(' ', 2).ElementAtOrDefault(1)));
        var written = items.Where(item => item.Path is not null && item.Flags.Length >= 9 && item.Flags[0] is '<' or '>' or 'c' && item.Flags[1] is 'f' or 'L').Select(item => item.Path!);

        return [.. deleted.Concat(written).Where(file => !file.EndsWith('/'))];
    }

    /// <summary>The bytes rsync's stats say went over the wire.</summary>
    public static long TransferredBytes(string output)
    {
        var line = output.Split('\n').FirstOrDefault(line => line.StartsWith("Total transferred file size:", StringComparison.Ordinal));
        var number = line?.Split(':', 2)[1].Trim().Split(' ')[0].Replace(",", string.Empty, StringComparison.Ordinal);

        return long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes) ? bytes : 0;
    }

    /// <summary>Why a run did not end well, in a line.</summary>
    public static string Reason(ProgramRun run) => run.IsTimedOut ? "it ran past its time" : string.IsNullOrWhiteSpace(run.Error) ? $"exit code {run.ExitCode}" : run.Error.Trim();
}
