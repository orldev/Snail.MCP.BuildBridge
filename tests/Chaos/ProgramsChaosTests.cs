using System.Diagnostics;
using Snail.MCP.BuildBridge.Adapters;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Chaos;

/// <summary>The process runner under programs that misbehave: one that dies before reading its input, one that reads input nobody sends, a hundred at once.</summary>
public sealed class ProgramsChaosTests
{
    private static readonly Programs Programs = new();

    /// <summary>
    /// Breaks the write of the excludes to a program's standard input. rsync that fails to connect exits before it reads
    /// its input; past the pipe's 64 KB the write meets a closed pipe, and an IOException from inside RunAsync would
    /// reach the tool as a crash instead of an answer with rsync's exit code. The input here is bigger than any pipe buffer.
    /// </summary>
    [Fact]
    public async Task RunAsync_ProgramExitsBeforeReadingALargeInput_AnswersInsteadOfThrowing()
    {
        string input = new('x', 4 * 1024 * 1024);

        var run = await Programs.RunAsync("/usr/bin/true", [], null, TimeSpan.FromSeconds(10), CancellationToken.None, input);

        Assert.False(run.IsTimedOut);
        Assert.False(run.IsMissing);
    }

    /// <summary>
    /// A program that reads its standard input, run to a log: the log runner never closes the input it redirected, so
    /// the program waits for an end that never comes and ends only by the timeout. cat should end at once with no input.
    /// </summary>
    [Fact]
    public async Task RunToLogAsync_ProgramReadingItsInput_EndsWithoutWaitingForTheTimeout()
    {
        string log = Path.Combine(TestFolder.Create(), "cat.log");
        var started = Stopwatch.StartNew();

        var run = await Programs.RunToLogAsync("/bin/cat", [], null, log, TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.False(run.IsTimedOut, $"cat waited for input for {started.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(0, run.ExitCode);
    }

    /// <summary>A hundred programs at once: every one ends, none is lost to a shared handle or a race in the runner.</summary>
    [Fact]
    public async Task RunAsync_HundredAtOnce_EveryOneAnswers()
    {
        var runs = Enumerable.Range(0, 100).Select(number => Programs.RunAsync("/bin/echo", [$"{number}"], null, TimeSpan.FromSeconds(30), CancellationToken.None));

        var answered = await Task.WhenAll(runs);

        Assert.All(answered, run => Assert.Equal(0, run.ExitCode));
        Assert.Equal(100, answered.Select(run => run.Output.Trim()).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A program stopped by the timeout leaves nothing of its own behind: the whole tree goes with it on this machine.</summary>
    [Fact]
    public async Task RunAsync_TimedOutProgramWithAChild_LeavesNoChildRunning()
    {
        string word = $"s3l-chaos-{Path.GetRandomFileName()[..6]}";

        var run = await Programs.RunAsync("/bin/sh", ["-c", $"sh -c 'sleep 300 # {word}; true' & wait"], null, TimeSpan.FromSeconds(1), CancellationToken.None);
        await Task.Delay(300);
        var alive = await Programs.RunAsync("/usr/bin/pgrep", ["-f", word], null, TimeSpan.FromSeconds(10), CancellationToken.None);

        await Programs.RunAsync("/usr/bin/pkill", ["-f", word], null, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(run.IsTimedOut);
        Assert.Equal(string.Empty, alive.Output.Trim());
    }
}
