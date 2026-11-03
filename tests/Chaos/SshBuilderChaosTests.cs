using System.Diagnostics;
using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tests.Support;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Chaos;

/// <summary>The builder over a real host under a command that outlives its time, and the log reader under a log that is too big.</summary>
public sealed class SshBuilderChaosTests
{
    private static readonly Programs Programs = new();

    private static SshBuilder Builder()
    {
        var options = Options.Create(new BuildOptions { RunSeconds = 10, TransferSeconds = 60 });
        var shell = new RemoteShell(options);

        return new SshBuilder(Programs, new RsyncCourier(Programs, new GitCopies(Programs), shell, options, TimeProvider.System), shell, options, TimeProvider.System);
    }

    /// <summary>
    /// Breaks the stop of a timed-out run on a real host. The run's script kills the command and its direct children
    /// by pid; a grandchild — dotnet's testhost under dotnet test, a compiler under a script — is nobody's child by
    /// then and keeps running on the host. The whole process group must go.
    /// </summary>
    [HostFact]
    public async Task ExecuteAsync_TimedOutCommandWithAGrandchild_LeavesNothingRunningOnTheHost()
    {
        var host = new Host("real", HostFactAttribute.Address);
        var copy = (await new GitCopies(Programs).ReadAsync(Repositories.Create("S3lBuildBridgeChaos"), CancellationToken.None)).Value!;
        var builder = Builder();
        string word = $"s3l-chaos-{Path.GetRandomFileName()[..6]}";
        string log = Path.Combine(TestFolder.Create(), "run.log");
        await builder.DeliverAsync(host, copy, false, CancellationToken.None);

        var ran = await builder.ExecuteAsync(host, copy.Workspace, ["sh", "-c", $"sh -c 'sleep 300 # {word}; true'; true"], log, TimeSpan.FromSeconds(2), CancellationToken.None);
        await Task.Delay(500);
        var alive = await Programs.RunAsync("ssh", ["-o", "BatchMode=yes", host.Address, $"pgrep -f '{word}' || true"], null, TimeSpan.FromSeconds(30), CancellationToken.None);

        await Programs.RunAsync("ssh", ["-o", "BatchMode=yes", host.Address, $"pkill -f '{word}' || true"], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        await builder.RemoveAsync(host, copy.Workspace, CancellationToken.None);
        Assert.True(ran.Value!.IsTimedOut);
        Assert.Equal(string.Empty, alive.Output.Trim());
    }

    /// <summary>
    /// A log of a hundred megabytes, as a verbose build writes, read the way build_logs reads it: every line into
    /// memory to answer with the last hundred. The tail must come in well under a second and without the whole file.
    /// </summary>
    [Fact]
    public async Task LogTail_HundredMegabytes_IsReadWithoutTheWholeFile()
    {
        string log = Path.Combine(TestFolder.Create(), "big.log");
        await using (var file = File.CreateText(log))
        {
            for (int line = 0; line < 1_000_000; line++)
                await file.WriteLineAsync($"line {line} of a build that says the same thing over and over again, as compilers do");
        }
        long before = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.StartNew();

        var tail = await Logs.TailAsync(log, 100, CancellationToken.None);

        long allocated = GC.GetTotalAllocatedBytes(true) - before;
        Assert.Equal(100, tail.Count);
        Assert.StartsWith("line 999999 ", tail[^1], StringComparison.Ordinal);
        Assert.True(allocated < 16 * 1024 * 1024, $"reading the tail allocated {allocated / 1024 / 1024} MB in {started.ElapsedMilliseconds} ms");
    }
}
