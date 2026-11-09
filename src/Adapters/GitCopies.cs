using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

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
            return Answer.Failed(Messages.ProgramMissing("git"), type: Refusals.NotInstalled);

        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!run.IsSuccess || lines.Length < 5)
            return Answer.Failed(Messages.NotACopy(path), type: Refusals.NotACopy);

        string top = lines[0];
        bool isWorktree = !string.Equals(lines[1], lines[2], StringComparison.Ordinal);
        string commit = lines[3];
        string reference = lines[4];
        string branch = reference == "HEAD" ? commit[..Math.Min(7, commit.Length)] : reference;

        return Answer.Ok(new LocalCopy(top, Path.GetFileName(top), branch, commit, isWorktree));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The list of what git ignores names only what exists here; a folder the host makes for itself in an ignored
    /// path absent here is not in it, and a one-way session would delete it. The patterns themselves cover that: a
    /// gitignore pattern without a slash matches at any depth under its file's folder, one with a slash is anchored
    /// there, a trailing slash means a folder, a leading ! negates — and Mutagen's syntax says the same with ** and /.
    /// </remarks>
    public async Task<IReadOnlyList<string>> IgnorePatternsAsync(LocalCopy copy, CancellationToken cancellationToken)
    {
        var run = await programs.RunAsync("git", ["-C", copy.Path, "ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", ".gitignore", "*/.gitignore"], null, Patience, cancellationToken);
        if (!run.IsSuccess)
            return [];

        var patterns = new List<string>();
        foreach (var file in run.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            string folder = Path.GetDirectoryName(file)?.Replace('\\', '/') ?? string.Empty;
            var lines = await File.ReadAllLinesAsync(Path.Combine(copy.Path, file), cancellationToken);
            patterns.AddRange(lines.Select(line => Translated(folder, line)).OfType<string>());
        }

        return patterns;
    }

    /// <summary>A gitignore line as a Mutagen pattern anchored at <paramref name="folder"/>, or null for a blank or a comment.</summary>
    internal static string? Translated(string folder, string line)
    {
        string pattern = line.TrimEnd();
        if (pattern.Length == 0 || pattern.StartsWith('#'))
            return null;

        bool isNegated = pattern.StartsWith('!');
        string body = isNegated ? pattern[1..] : pattern;
        bool isAnchored = body.TrimEnd('/').Contains('/');
        string relative = body.TrimStart('/');
        string under = folder.Length == 0 ? string.Empty : $"{folder}/";
        string anchored = isAnchored ? $"/{under}{relative}" : $"/{under}**/{relative}";

        return isNegated ? $"!{anchored}" : anchored;
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
