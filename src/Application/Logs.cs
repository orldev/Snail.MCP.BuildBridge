using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>Where the log of each job goes on this machine: the server's data folder, one file per job.</summary>
public sealed class Logs(TimeProvider clock)
{
    /// <summary>The folder the logs are in.</summary>
    public string Folder { get; } = Path.Combine(ServerFolders.DataDirectory(BuildSettings.Name), "jobs");

    /// <summary>A new log file for work of <paramref name="kind"/>, named by the moment and a random word so two jobs never share one.</summary>
    public string New(string kind) =>
        Path.Combine(Folder, $"{clock.GetUtcNow():yyyyMMdd-HHmmss}-{kind}-{Path.GetRandomFileName()[..6]}.log");
}
