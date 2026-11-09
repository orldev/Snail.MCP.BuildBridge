using Snail.Sdk.Mcp.Hosting.Answers;


namespace Snail.MCP.BuildBridge.Application.Ports;

/// <summary>The secrets of this machine a run gets in its environment: named in the settings, valued here, never kept on a host.</summary>
public interface ISecrets
{
    /// <summary>The variables a run of <paramref name="repository"/> gets, by name; or why one could not be read.</summary>
    Task<Answer<IReadOnlyDictionary<string, string>>> ForAsync(string repository, CancellationToken cancellationToken);
}
