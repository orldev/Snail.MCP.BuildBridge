namespace Snail.MCP.BuildBridge.Domain;

/// <summary>Which workspace on a host: one per repository and branch, so two sessions on one branch share a copy and its build outputs.</summary>
/// <param name="Repository">The repository's name.</param>
/// <param name="Branch">The branch, as the local copy had it.</param>
public readonly record struct WorkspaceKey(string Repository, string Branch)
{
    /// <summary>The folder under a host's root: <c>workspaces/&lt;repository&gt;/&lt;branch&gt;</c>, with the characters either may carry that a folder, a shell or rsync may not replaced.</summary>
    public string Folder => $"workspaces/{Safe(Repository)}/{Safe(Branch)}";

    /// <summary>A branch as a folder name: every slash and character a path cannot carry becomes a dash.</summary>
    public static string Safe(string branch)
    {
        var letters = branch.Select(letter => char.IsLetterOrDigit(letter) || letter is '.' or '_' or '-' ? letter : '-');

        return new string([.. letters]);
    }
}

/// <summary>What this server wrote beside a workspace to prove it is its own: where it came from and when it was last built.</summary>
/// <param name="Key">The workspace.</param>
/// <param name="Origin">This machine and the copy's path, so a workspace can be traced back.</param>
/// <param name="Commit">The commit last delivered, when known.</param>
/// <param name="DeliveredAt">When the copy was last delivered.</param>
/// <param name="BuiltAt">When a build last ran in it; null before the first.</param>
public sealed record WorkspaceMarker(WorkspaceKey Key, string Origin, string? Commit, DateTimeOffset DeliveredAt, DateTimeOffset? BuiltAt)
{
    /// <summary>The marker's file name inside the workspace.</summary>
    public const string FileName = ".s3l-build.json";

    /// <summary>The file a run touches beside the marker, whose time is when the workspace was last built.</summary>
    public const string BuiltFileName = ".s3l-build.built";
}
