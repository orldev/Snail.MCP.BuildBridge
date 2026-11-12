using System.Globalization;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>How rsync is called and read: the arguments of a mirror, and the files and bytes its output names.</summary>
internal static class Rsync
{
    private const string TransferredPrefix = "Total transferred file size:";

    /// <summary>The arguments of a mirror from <paramref name="source"/> to <paramref name="destination"/>, over <paramref name="ssh"/> — the program with its options, in a line — unless the host is this machine, with excludes on the standard input when asked.</summary>
    public static IReadOnlyList<string> Arguments(string ssh, Host host, bool isDryRun, bool hasExcludes, string source, string destination)
    {
        var arguments = new List<string> { "-a", "--delete", "--itemize-changes", "--stats" };
        if (isDryRun)
            arguments.Add("--dry-run");
        if (hasExcludes)
            arguments.Add("--exclude-from=-");
        if (!host.IsLocal)
            arguments.AddRange(["-e", ssh]);

        arguments.Add(source);
        arguments.Add(destination);

        return arguments;
    }

    /// <summary>The files an itemized rsync run named: created, changed or deleted.</summary>
    /// <remarks>
    /// An item is its flags, a space and the path; openrsync writes nine flag characters where rsync 3 writes eleven, so
    /// the space is looked for rather than counted to. The output is read line by line over its span, and only a path
    /// kept becomes a string: splitting ten thousand lines into strings, twice over, cost 4.5 MB a delivery
    /// (ParsingBenchmarks, 2026-11-03).
    /// </remarks>
    public static IReadOnlyList<string> Changed(string output)
    {
        var files = new List<string>();
        foreach (var line in output.AsSpan().EnumerateLines())
        {
            if (FileOf(line) is { } file)
                files.Add(file);
        }

        return files;
    }

    /// <summary>The bytes rsync's stats say went over the wire.</summary>
    public static long TransferredBytes(string output)
    {
        foreach (var line in output.AsSpan().EnumerateLines())
        {
            if (line.StartsWith(TransferredPrefix, StringComparison.Ordinal))
                return BytesIn(line[TransferredPrefix.Length..]);
        }

        return 0;
    }

    /// <summary>Why a run did not end well, in a line.</summary>
    public static string Reason(ProgramRun run) => run.IsTimedOut ? "it ran past its time" : string.IsNullOrWhiteSpace(run.Error) ? $"exit code {run.ExitCode}" : run.Error.Trim();

    private static string? FileOf(ReadOnlySpan<char> line)
    {
        if (line.StartsWith("*deleting", StringComparison.Ordinal))
            return Named(line[9..].Trim());

        int space = line.IndexOf(' ');
        if (space < 9)
            return null;

        bool isWritten = line[0] is '<' or '>' or 'c' && line[1] is 'f' or 'L';

        return isWritten ? Named(line[(space + 1)..]) : null;
    }

    /// <summary>A path as a file, or null for a folder or nothing.</summary>
    private static string? Named(ReadOnlySpan<char> path) => path.IsEmpty || path.EndsWith('/') ? null : path.ToString();

    /// <summary>The number the stats line carries — <c>1,234,567 B</c> — as a count, or 0 when there is none.</summary>
    private static long BytesIn(ReadOnlySpan<char> rest)
    {
        var number = rest.Trim();
        int end = number.IndexOf(' ');
        if (end >= 0)
            number = number[..end];

        return long.TryParse(number, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out long bytes) ? bytes : 0;
    }
}
