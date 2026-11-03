namespace Snail.MCP.BuildBridge.Domain;

/// <summary>A copy delivered to a host's workspace: what moved, or would move.</summary>
/// <param name="Key">The workspace it landed in.</param>
/// <param name="Files">The files sent, or listed when nothing was sent.</param>
/// <param name="Bytes">The bytes that went over the wire; 0 when nothing was sent.</param>
/// <param name="Took">How long the delivery took.</param>
/// <param name="IsDryRun">Whether the files were only listed, not sent.</param>
public sealed record Delivery(WorkspaceKey Key, IReadOnlyList<string> Files, long Bytes, TimeSpan Took, bool IsDryRun);

/// <summary>A command as it ran on a host: how it ended and where its output went.</summary>
/// <param name="ExitCode">The exit code; -1 when the command did not end by itself.</param>
/// <param name="Log">The file on this machine holding everything the command wrote.</param>
/// <param name="Took">How long it ran.</param>
/// <param name="IsTimedOut">Whether it was stopped for running past its time.</param>
public sealed record Execution(int ExitCode, string Log, TimeSpan Took, bool IsTimedOut)
{
    /// <summary>Whether the command ended as it meant to.</summary>
    public bool IsSuccess => ExitCode == 0 && !IsTimedOut;
}

/// <summary>Paths brought back from a workspace to this machine.</summary>
/// <param name="Folder">The folder on this machine they were put in.</param>
/// <param name="Files">Every file brought, relative to that folder.</param>
/// <param name="Bytes">The bytes that came over the wire.</param>
public sealed record Collected(string Folder, IReadOnlyList<string> Files, long Bytes);

/// <summary>A program as it ran: how it ended and what it wrote.</summary>
/// <param name="ExitCode">The exit code; -1 when it did not end by itself.</param>
/// <param name="Output">What it wrote to its standard output.</param>
/// <param name="Error">What it wrote to its standard error.</param>
/// <param name="IsMissing">Whether the program is not installed, which is an answer and not a failure.</param>
/// <param name="IsTimedOut">Whether it was stopped for running past its time.</param>
public sealed record ProgramRun(int ExitCode, string Output, string Error, bool IsMissing = false, bool IsTimedOut = false)
{
    /// <summary>Whether the program ended as it meant to.</summary>
    public bool IsSuccess => ExitCode == 0 && !IsMissing && !IsTimedOut;

    /// <summary>A program that is not installed.</summary>
    public static ProgramRun Missing(string program) => new(-1, string.Empty, $"{program} is not installed.", IsMissing: true);
}
