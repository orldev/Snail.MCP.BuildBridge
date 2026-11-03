using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Application.Ports;

/// <summary>The outside programs this server works through: ssh, rsync, git.</summary>
public interface IPrograms
{
    /// <summary>Runs a program to the end, or until its time is up, and says how it ended and what it wrote; <paramref name="input"/> is what it reads on its standard input, then the end of it.</summary>
    Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, string? directory, TimeSpan timeout, CancellationToken cancellationToken, string? input = null);

    /// <summary>Runs a program with everything it writes going to <paramref name="log"/> as it is written, so the file can be read while it still runs.</summary>
    Task<ProgramRun> RunToLogAsync(string program, IReadOnlyList<string> arguments, string? directory, string log, TimeSpan timeout, CancellationToken cancellationToken);
}
