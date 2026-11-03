using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>Working copies as git reports them: one rev-parse for the root, branch, commit and whether it is a worktree, one ls-files for what is ignored.</summary>
/// <remarks>
/// A linked worktree is told from the main copy by its git directory lying elsewhere than the common one: there
/// <c>.git</c> is a file naming a folder of the main repository, which means nothing on another machine. The commit
/// is asked for before <c>--abbrev-ref</c>, which abbreviates every revision after it: asked the other way round, git
/// answered the branch twice (2026-11-03).
/// </remarks>
public sealed class GitCopies(IPrograms programs) : ICopies
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    public async Task<Answer<LocalCopy>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var run = await programs.RunAsync("git", ["-C", path, "rev-parse", "--path-format=absolute", "--show-toplevel", "--git-dir", "--git-common-dir", "HEAD", "--abbrev-ref", "HEAD"], null, Patience, cancellationToken);
        if (run.IsMissing)
            return Answer.Failed<LocalCopy>(Messages.ProgramMissing("git"));

        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!run.IsSuccess || lines.Length < 5)
            return Answer.Failed<LocalCopy>(Messages.NotACopy(path));

        string top = lines[0];
        bool isWorktree = !string.Equals(lines[1], lines[2], StringComparison.Ordinal);
        string commit = lines[3];
        string reference = lines[4];
        string branch = reference == "HEAD" ? commit[..Math.Min(7, commit.Length)] : reference;

        return Answer.Ok(new LocalCopy(top, Path.GetFileName(top), branch, commit, isWorktree));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> IgnoredAsync(LocalCopy copy, CancellationToken cancellationToken)
    {
        var run = await programs.RunAsync("git", ["-C", copy.Path, "ls-files", "--others", "--ignored", "--exclude-standard", "--directory", "-z"], null, Patience, cancellationToken);
        if (!run.IsSuccess)
            return [];

        return run.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
