using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>Paths of a workspace brought back to this machine: test results, a bundle for a simulator, a package.</summary>
public sealed class Collections(Hosts hosts, ICopies copies, IBuilder builder)
{
    /// <summary>The folder fetched paths land in unless one is named: the server's data folder, by repository and branch.</summary>
    public static string FolderOf(WorkspaceKey key) =>
        Path.Combine(ServerFolders.DataDirectory(BuildSettings.Name), "fetched", WorkspaceKey.Safe(key.Repository), WorkspaceKey.Safe(key.Branch));

    /// <summary>Brings <paramref name="paths"/> of the workspace of the copy <paramref name="path"/> lies in, from the host named or the first, into <paramref name="into"/> or the folder of its own.</summary>
    public async Task<Answer<Collected>> FetchAsync(string path, string? host, IReadOnlyList<string> paths, string? into, CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
            return Answer.Failed(Messages.NothingToFetch, type: Refusals.Arguments);

        var copy = await copies.ReadAsync(path, cancellationToken);
        if (!copy.TryGet(out var found))
            return copy.As<Collected>();

        var ready = await hosts.ReadyAsync(host, [], cancellationToken);
        if (!ready.TryGet(out var state))
            return ready.As<Collected>();

        string folder = string.IsNullOrWhiteSpace(into) ? FolderOf(found.Workspace) : into;

        return await builder.CollectAsync(state.Host, found.Workspace, paths, folder, cancellationToken);
    }
}
