namespace Snail.MCP.BuildBridge.Domain;

/// <summary>A working copy on this machine, as it is about to be delivered: where it is, which repository and branch it is.</summary>
/// <param name="Path">The root of the working copy, an absolute path.</param>
/// <param name="Repository">The repository's name, which names its folder of workspaces on a host.</param>
/// <param name="Branch">The branch checked out, which names the workspace; detached, the commit's short id.</param>
/// <param name="Commit">The commit checked out, when git could say.</param>
/// <param name="IsWorktree">Whether the copy is a linked worktree, whose .git is a file pointing into the main repository rather than a folder.</param>
public sealed record LocalCopy(string Path, string Repository, string Branch, string? Commit, bool IsWorktree)
{
    /// <summary>The workspace this copy lands in on a host.</summary>
    public WorkspaceKey Workspace => new(Repository, Branch);
}
