using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Application.Ports;

/// <summary>Working copies on this machine, as git knows them.</summary>
public interface ICopies
{
    /// <summary>The copy a path lies in: its root, repository, branch and commit; or why there is none.</summary>
    Task<Answer<LocalCopy>> ReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>What git ignores in a copy, relative to its root, an ignored folder as one entry with a trailing slash: what a delivery leaves out.</summary>
    Task<IReadOnlyList<string>> IgnoredAsync(LocalCopy copy, CancellationToken cancellationToken);

    /// <summary>The patterns of every .gitignore of the copy, each anchored at its own folder, in the syntax Mutagen reads: what would be ignored even where nothing is there yet.</summary>
    Task<IReadOnlyList<string>> IgnorePatternsAsync(LocalCopy copy, CancellationToken cancellationToken);
}
