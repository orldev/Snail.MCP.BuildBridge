using Snail.MCP.BuildBridge.Adapters;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

public sealed class ProgramsTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RunAsync_ProgramThatEnds_AnswersItsOutputAndCode()
    {
        var run = await new Programs().RunAsync("/bin/sh", ["-c", "echo out; echo err >&2; exit 3"], null, Patience, CancellationToken.None);

        Assert.Equal(3, run.ExitCode);
        Assert.Equal("out\n", run.Output);
        Assert.Equal("err\n", run.Error);
        Assert.False(run.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_Input_IsWhatTheProgramReads()
    {
        var run = await new Programs().RunAsync("/bin/cat", [], null, Patience, CancellationToken.None, "fed\n");

        Assert.Equal("fed\n", run.Output);
    }

    [Fact]
    public async Task RunAsync_ProgramNotInstalled_IsAnAnswerNotAnException()
    {
        var run = await new Programs().RunAsync("/no/such/program", [], null, Patience, CancellationToken.None);

        Assert.True(run.IsMissing);
        Assert.False(run.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_ProgramPastItsTime_IsStoppedAndSaysSo()
    {
        var run = await new Programs().RunAsync("/bin/sleep", ["30"], null, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.True(run.IsTimedOut);
    }

    [Fact]
    public async Task RunToLogAsync_OutputAndErrors_LandInTheLogInOrder()
    {
        string log = Path.Combine(TestFolder.Create(), "run.log");

        var run = await new Programs().RunToLogAsync("/bin/sh", ["-c", "echo one; echo two >&2; echo three; exit 5"], null, log, Patience, CancellationToken.None);

        Assert.Equal(5, run.ExitCode);
        Assert.Equal("one\ntwo\nthree\n", await File.ReadAllTextAsync(log));
    }

    /// <summary>What a run is given on its standard input reaches the program whole, then the end of it: the way variables travel to a host's shell without touching its command line.</summary>
    [Fact]
    public async Task RunToLogAsync_WithInput_FeedsItToTheProgramThenEndsIt()
    {
        string log = Path.Combine(TestFolder.Create(), "run.log");

        var run = await new Programs().RunToLogAsync("/bin/sh", ["-c", "eval \"$(cat)\"; printf '%s\\n' \"$TOKEN\""], null, log, Patience, CancellationToken.None, "export TOKEN='s3cr3t it'\\''s'\n");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("s3cr3t it's\n", await File.ReadAllTextAsync(log));
    }

    [Fact]
    public async Task RunToLogAsync_ProgramPastItsTime_KeepsWhatItWroteSoFar()
    {
        string log = Path.Combine(TestFolder.Create(), "run.log");

        var run = await new Programs().RunToLogAsync("/bin/sh", ["-c", "echo started; sleep 30"], null, log, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(run.IsTimedOut);
        Assert.Equal("started\n", await File.ReadAllTextAsync(log));
    }
}
