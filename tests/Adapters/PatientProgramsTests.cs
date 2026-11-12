using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

/// <summary>A host that closes a connection before its greeting never started the command, so the command is run again after a pause; any other ending is the answer.</summary>
public sealed class PatientProgramsTests
{
    /// <summary>Programs that end as the test lined up, one ending per attempt, the last one again once they run out.</summary>
    private sealed class LinedUpPrograms(params ProgramRun[] endings) : IPrograms
    {
        public int Attempts { get; private set; }

        public Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, string? directory, TimeSpan timeout, CancellationToken cancellationToken, string? input = null) =>
            Task.FromResult(endings[Math.Min(Attempts++, endings.Length - 1)]);

        public async Task<ProgramRun> RunToLogAsync(string program, IReadOnlyList<string> arguments, string? directory, string log, TimeSpan timeout, CancellationToken cancellationToken, string? input = null)
        {
            var run = endings[Math.Min(Attempts++, endings.Length - 1)];
            await File.AppendAllTextAsync(log, run.Output + run.Error, cancellationToken);

            return run;
        }
    }

    private static readonly ProgramRun Ended = new(0, "ok", string.Empty);

    private static readonly ProgramRun TurnedAway = new(255, string.Empty, "kex_exchange_identification: Connection closed by remote host\nConnection closed by 192.0.2.10 port 22");

    /// <summary>A clock whose every wait is over at once, and remembered.</summary>
    private sealed class ImpatientClock : TimeProvider
    {
        public List<TimeSpan> Waited { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waited.Add(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));

            return new SpentTimer();
        }

        private sealed class SpentTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static Task<ProgramRun> RunAsync(PatientPrograms programs) =>
        programs.RunAsync("ssh", ["mini", "true"], null, TimeSpan.FromSeconds(20), CancellationToken.None);

    [Fact]
    public async Task RunAsync_TurnedAwayBeforeTheGreetingThenLetIn_AnswersTheRunThatWentThrough()
    {
        var inner = new LinedUpPrograms(TurnedAway, TurnedAway, Ended);
        var clock = new ImpatientClock();

        var run = await RunAsync(new PatientPrograms(inner, clock));

        Assert.True(run.IsSuccess);
        Assert.Equal(3, inner.Attempts);
        Assert.Equal(PatientPrograms.Pauses.Take(2), clock.Waited);
    }

    [Fact]
    public async Task RunAsync_TurnedAwayEveryTime_GivesUpAfterItsPausesWithTheLastAnswer()
    {
        var inner = new LinedUpPrograms(TurnedAway);
        var run = await RunAsync(new PatientPrograms(inner, new ImpatientClock()));

        Assert.Equal(TurnedAway, run);
        Assert.Equal(PatientPrograms.Pauses.Count + 1, inner.Attempts);
    }

    [Theory]
    [InlineData(1, "error CS1002: ; expected")]
    [InlineData(255, "Connection to 192.0.2.10 closed by remote host.")]
    [InlineData(255, "Read from remote host 192.0.2.10: Connection reset by peer")]
    [InlineData(255, "Permission denied (publickey).")]
    public async Task RunAsync_AFailureOfTheCommandOrALinkLostMidway_IsNotRunAgain(int exitCode, string error)
    {
        var inner = new LinedUpPrograms(new ProgramRun(exitCode, string.Empty, error), Ended);
        var run = await RunAsync(new PatientPrograms(inner, new ImpatientClock()));

        Assert.False(run.IsSuccess);
        Assert.Equal(1, inner.Attempts);
    }

    /// <summary>A build has a connection of its own and can be turned away like any command: when all the attempt wrote is ssh's few words, it is run again and those words leave the log.</summary>
    [Fact]
    public async Task RunToLogAsync_TurnedAwayBeforeTheBuildBegan_RunsItAgainAndKeepsTheLogClean()
    {
        var inner = new LinedUpPrograms(TurnedAway, new ProgramRun(0, "Build succeeded.\n", string.Empty));
        string log = Path.Combine(TestFolder.Create(), "run.log");
        await File.WriteAllTextAsync(log, "generated the project\n", CancellationToken.None);

        var run = await new PatientPrograms(inner, new ImpatientClock()).RunToLogAsync("ssh", ["mini", "true"], null, log, TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(2, inner.Attempts);
        Assert.Equal("generated the project\nBuild succeeded.\n", await File.ReadAllTextAsync(log, CancellationToken.None));
    }

    /// <summary>A build that wrote its output had begun, whatever its last words: it is never started twice.</summary>
    [Fact]
    public async Task RunToLogAsync_ABuildThatBeganAndLostItsLink_IsNotRunAgain()
    {
        var lost = new ProgramRun(255, new string('x', 2000) + "\n", "Connection closed by 192.0.2.10 port 22");
        var inner = new LinedUpPrograms(lost, Ended);
        string log = Path.Combine(TestFolder.Create(), "run.log");

        var run = await new PatientPrograms(inner, new ImpatientClock()).RunToLogAsync("ssh", ["mini", "true"], null, log, TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Equal(lost, run);
        Assert.Equal(1, inner.Attempts);
    }
}
