using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>Programs run again when ssh was turned away before the host's greeting: the command never started, so nothing is done twice.</summary>
/// <remarks>
/// A Mac's sshd is started by launchd for each connection and takes forty-two handshakes at a time. When the host's
/// sshd restarts, every Mutagen session of this machine reconnects at once, and a connection made in that minute is
/// closed before the greeting — four runs ended that way on 2026-11-07. The same words come from ssh under rsync and
/// under mutagen. A connection lost once the command was running reads otherwise — <c>Connection to … closed by
/// remote host</c>, <c>Read from remote host …</c> — and is not tried again. A build written to its log shares a
/// connection with other builds and opens one when there is none, so it can be turned away as well: it is run again only when ssh ended with its own code
/// and all the attempt added to the log is those few words, which are then taken out of it.
/// </remarks>
public sealed class PatientPrograms(IPrograms programs, TimeProvider clock) : IPrograms
{
    /// <summary>How long is waited before each further attempt.</summary>
    internal static IReadOnlyList<TimeSpan> Pauses { get; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    /// <summary>The most bytes ssh's words about a connection it could not make come to.</summary>
    private const int MostWords = 1024;

    /// <summary>The exit code ssh ends with when the failure is its own.</summary>
    private const int SshFailed = 255;

    /// <summary>What ssh says of a connection lost after the command began.</summary>
    private const string MidwayWords = "Read from remote host";

    private static readonly string[] TurnedAwayWords = ["kex_exchange_identification", "Connection closed by", "Connection reset by", "Connection refused", "banner exchange"];

    /// <inheritdoc/>
    public async Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, string? directory, TimeSpan timeout, CancellationToken cancellationToken, string? input = null)
    {
        var run = await programs.RunAsync(program, arguments, directory, timeout, cancellationToken, input);
        foreach (var pause in Pauses)
        {
            if (!IsTurnedAway(run))
                break;

            await Task.Delay(pause, clock, cancellationToken);
            run = await programs.RunAsync(program, arguments, directory, timeout, cancellationToken, input);
        }

        return run;
    }

    /// <inheritdoc/>
    public async Task<ProgramRun> RunToLogAsync(string program, IReadOnlyList<string> arguments, string? directory, string log, TimeSpan timeout, CancellationToken cancellationToken, string? input = null)
    {
        long before = LengthOf(log);
        var run = await programs.RunToLogAsync(program, arguments, directory, log, timeout, cancellationToken, input);
        foreach (var pause in Pauses)
        {
            if (run.ExitCode != SshFailed || !IsTurnedAway(run with { Error = await AddedAsync(log, before, cancellationToken) }))
                break;

            Cut(log, before);
            await Task.Delay(pause, clock, cancellationToken);
            run = await programs.RunToLogAsync(program, arguments, directory, log, timeout, cancellationToken, input);
        }

        return run;
    }

    private static long LengthOf(string log) => File.Exists(log) ? new FileInfo(log).Length : 0;

    /// <summary>What an attempt added to the log, when it is as little as ssh's own words; nothing when it is more, since a command that wrote that much had begun.</summary>
    private static async Task<string> AddedAsync(string log, long before, CancellationToken cancellationToken)
    {
        long added = LengthOf(log) - before;
        if (added is <= 0 or > MostWords)
            return string.Empty;

        await using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        file.Seek(before, SeekOrigin.Begin);
        using var reader = new StreamReader(file);

        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static void Cut(string log, long length)
    {
        using var file = new FileStream(log, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        file.SetLength(length);
    }

    /// <summary>Whether the run failed because a host closed the connection before ssh was greeted.</summary>
    internal static bool IsTurnedAway(ProgramRun run)
    {
        if (run.IsSuccess || run.IsMissing || run.IsTimedOut || run.Error.Contains(MidwayWords, StringComparison.Ordinal))
            return false;

        return TurnedAwayWords.Any(words => run.Error.Contains(words, StringComparison.Ordinal));
    }
}
