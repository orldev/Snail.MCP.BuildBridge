using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>The NuGet feeds of this machine kept mirrored on a host, sent again only once one of them changed.</summary>
/// <remarks>
/// Mirroring a feed before every run was 1.4 s of a run whose command took 0.5 s — a folder made and an rsync that
/// found nothing to send (2026-11-12). What a feed holds is told by how many files it has and the newest of their
/// times; a file under the logs folder keeps that for each host, so every process of this server knows. A feed is
/// mirrored again after an hour whatever it holds, since the host's copy can be erased without this machine hearing.
/// </remarks>
public sealed class Feeds(IBuilder builder, Logs logs, IOptions<BuildOptions> options, TimeProvider clock)
{
    private static readonly TimeSpan Trusted = TimeSpan.FromHours(1);

    /// <summary>Mirrors to <paramref name="host"/> every feed the settings name that changed since it was last mirrored there; the first that does not arrive is the answer.</summary>
    public async Task<Answer<bool>> MirrorAsync(Host host, CancellationToken cancellationToken)
    {
        foreach (var feed in options.Value.Feeds)
        {
            string holds = Held(feed.Path);
            string file = Path.Combine(logs.Folder, $".mirrored-{WorkspaceKey.Safe(host.Name)}-{WorkspaceKey.Safe(feed.Name)}");
            if (IsMirrored(file, holds))
                continue;

            var mirrored = await builder.MirrorAsync(host, feed.Path, $"feeds/{feed.Name}", cancellationToken);
            if (!mirrored.IsSuccess)
                return mirrored.As<bool>();

            Directory.CreateDirectory(logs.Folder);
            await File.WriteAllTextAsync(file, holds, cancellationToken);
            File.SetLastWriteTimeUtc(file, clock.GetUtcNow().UtcDateTime);
        }

        return Answer.Ok(true);
    }

    private bool IsMirrored(string file, string holds) =>
        File.Exists(file) && clock.GetUtcNow() - File.GetLastWriteTimeUtc(file) < Trusted && File.ReadAllText(file) == holds;

    /// <summary>What a feed's folder holds, in a line: the count of its files and the newest of their times; a folder that is not there holds nothing twice alike.</summary>
    private static string Held(string folder)
    {
        if (!Directory.Exists(folder))
            return Guid.NewGuid().ToString("N");

        long count = 0;
        long newest = 0;
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            count++;
            newest = Math.Max(newest, File.GetLastWriteTimeUtc(file).Ticks);
        }

        return $"{count}:{newest}";
    }
}
