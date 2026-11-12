using System.Text;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>Where the log of each job goes on this machine: the server's data folder, one file per job.</summary>
/// <param name="clock">The clock a log is named by.</param>
/// <param name="folder">The folder the logs go to; the server's data folder when not given — a test names one of its own, since tests once filled the developer's.</param>
public sealed class Logs(TimeProvider clock, string? folder = null)
{
    /// <summary>The folder the logs are in.</summary>
    public string Folder { get; } = folder ?? Path.Combine(ServerFolders.DataDirectory(BuildSettings.Name), "jobs");

    private const int Chunk = 64 * 1024;

    /// <summary>A new log file for work of <paramref name="kind"/>, named by the moment and a random word so two jobs never share one.</summary>
    public string New(string kind) =>
        Path.Combine(Folder, $"{clock.GetUtcNow():yyyyMMdd-HHmmss}-{kind}-{Path.GetRandomFileName()[..6]}.log");

    /// <summary>The last <paramref name="count"/> lines of <paramref name="log"/>, read from its end in chunks: a log of any size costs only its tail.</summary>
    /// <remarks>Measured before this: a log of 100 MB read whole for its last hundred lines allocated 279 MB and took 331 ms.</remarks>
    public static async Task<IReadOnlyList<string>> TailAsync(string log, int count, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
        var chunks = new List<byte[]>();
        long position = file.Length;
        int newlines = 0;

        while (position > 0 && newlines <= count)
        {
            int size = (int)Math.Min(Chunk, position);
            position -= size;
            file.Seek(position, SeekOrigin.Begin);
            byte[] chunk = new byte[size];
            await file.ReadExactlyAsync(chunk, cancellationToken);
            newlines += chunk.AsSpan().Count((byte)'\n');
            chunks.Add(chunk);
        }

        chunks.Reverse();
        string text = Encoding.UTF8.GetString([.. chunks.SelectMany(chunk => chunk)]);
        var lines = text.Split('\n');
        var kept = lines[^1].Length == 0 ? lines[..^1] : lines;

        return [.. kept.TakeLast(count)];
    }
}
