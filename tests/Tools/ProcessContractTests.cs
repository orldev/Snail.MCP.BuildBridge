using System.Text.Json;
using Snail.MCP.BuildBridge.Tests.Support;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Tools;

/// <summary>The server as a program with this machine as its only host: a run, its log and a sweep, over the protocol a client speaks.</summary>
/// <remarks>The server's home is the test helper's own, so its logs and fetched files land there and not in the developer's home.</remarks>
public sealed class ProcessContractTests
{
    private const string Assembly = "Snail.MCP.BuildBridge";

    private const string Variable = "SNAIL_BUILDBRIDGE_SERVER";

    private static Dictionary<string, string?> ThisMachine(string root) => new(StringComparer.Ordinal)
    {
        ["SNAIL_MCP_BUILDBRIDGE_HOSTS__0__NAME"] = "here",
        ["SNAIL_MCP_BUILDBRIDGE_HOSTS__0__ADDRESS"] = "localhost",
        ["SNAIL_MCP_BUILDBRIDGE_HOSTS__0__ROOT"] = root
    };

    [Fact]
    public async Task Hosts_ThisMachine_AnswersReachableWithDotnet()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable, ThisMachine(TestFolder.Create()));

        var answer = await client.CallToolAsync("build_hosts", new Dictionary<string, object?>());

        var host = answer.Data()!["hosts"]![0]!;
        Assert.True(answer.IsOk(), answer.Text());
        Assert.True(host["reachable"]!.GetValue<bool>());
        Assert.Equal(JsonValueKind.String, host["toolchains"]!["dotnet"]!.GetValueKind());
    }

    [MacFact]
    public async Task Run_DotnetVersionInACopy_EndsSucceededWithItsLogReadableAndItsWorkspaceSweepable()
    {
        string root = TestFolder.Create();
        string repository = Repositories.Create("Contract");
        await using var client = await ServerProgram.StartAsync(Assembly, Variable, ThisMachine(root));

        var ran = await client.CallToolAsync("build_run", new Dictionary<string, object?> { ["path"] = repository, ["kind"] = "dotnet", ["arguments"] = new[] { "--version" }, ["wait"] = 120 });
        var job = ran.Report()!;
        var result = job.Result!.Value;
        var read = await client.CallToolAsync("build_logs", new Dictionary<string, object?> { ["log"] = result.GetProperty("log").GetString() });
        var planned = await client.CallToolAsync("build_clean", new Dictionary<string, object?> { ["scope"] = "workspace", ["path"] = repository });
        string planId = planned.Data()!["planId"]!.GetValue<string>();
        var applied = await client.CallToolAsync("build_clean", new Dictionary<string, object?> { ["scope"] = "workspace", ["planId"] = planId });

        Assert.True(ran.IsOk(), ran.Text());
        Assert.Equal(JobState.Succeeded, job.State);
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Contains("10.", read.Data()!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Contract/main", planned.Data()!["items"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(1, applied.Data()!["done"]!.GetValue<int>());
        Assert.False(Directory.Exists(Path.Combine(root, "workspaces", "Contract", "main")));
    }

    /// <summary>A run that could not happen is a failed job, never a succeeded one whose result says ok false.</summary>
    [Fact]
    public async Task Run_AFolderThatIsNoCopy_EndsTheJobFailedAndAnswersTheFailure()
    {
        string folder = TestFolder.Create();
        await using var client = await ServerProgram.StartAsync(Assembly, Variable, ThisMachine(TestFolder.Create()));

        var ran = await client.CallToolAsync("build_run", new Dictionary<string, object?> { ["path"] = folder, ["kind"] = "dotnet", ["arguments"] = new[] { "--version" }, ["wait"] = 60 });
        var job = ran.Report()!;

        Assert.False(ran.IsOk(), ran.Text());
        Assert.True(ran.IsError);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("is not inside a git working copy", ran.Error(), StringComparison.Ordinal);
        Assert.Equal("notACopy", ran.Type());
        Assert.Equal(job.Message, ran.Error());
        Assert.Null(job.Result);
    }

    /// <summary>
    /// A link in the jobs folder that leads out of it is no log of this server: the folder was checked by its name, so
    /// the link passed and build_logs read whatever it named. Both sides are compared with every link followed — the
    /// home of a test lies under /var, which macOS links to /private/var — so a log beside it still reads.
    /// </summary>
    [Fact]
    public async Task Logs_ALinkOutOfTheJobsFolder_IsRefusedAsOutsideWhileALogBesideItReads()
    {
        string jobs = Path.Combine(ServerProgram.Home, ".s3l-mcp-buildbridge", "jobs");
        string secret = Path.Combine(TestFolder.Create(), "secret.txt");
        string log = Path.Combine(jobs, $"{Path.GetRandomFileName()}.log");
        string link = Path.Combine(jobs, $"{Path.GetRandomFileName()}.log");
        Directory.CreateDirectory(jobs);
        await File.WriteAllTextAsync(secret, "the secret\n");
        await File.WriteAllTextAsync(log, "first\nsecond\n");
        File.CreateSymbolicLink(link, secret);
        await using var client = await ServerProgram.StartAsync(Assembly, Variable, ThisMachine(TestFolder.Create()));

        var refused = await client.CallToolAsync("build_logs", new Dictionary<string, object?> { ["log"] = link });
        var read = await client.CallToolAsync("build_logs", new Dictionary<string, object?> { ["log"] = log });

        Assert.False(refused.IsOk(), refused.Text());
        Assert.Equal(Reached.Outside, refused.Type());
        Assert.DoesNotContain("the secret", refused.Text(), StringComparison.Ordinal);
        Assert.True(read.IsOk(), read.Text());
        Assert.Equal("first\nsecond", read.Data()!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task OwnRefusals_AsAProgram_CarryTheirType()
    {
        string missing = Path.Combine(ServerProgram.Home, ".s3l-mcp-buildbridge", "jobs", $"{Path.GetRandomFileName()}.log");
        await using var client = await ServerProgram.StartAsync(Assembly, Variable, ThisMachine(TestFolder.Create()));

        var gone = await client.CallToolAsync("build_logs", new Dictionary<string, object?> { ["log"] = missing });
        var elsewhere = await client.CallToolAsync("build_logs", new Dictionary<string, object?> { ["log"] = "/etc/hosts" });
        var unknown = await client.CallToolAsync("build_cancel", new Dictionary<string, object?> { ["job"] = "job://buildbridge/dotnet/none" });
        var kind = await client.CallToolAsync("build_run", new Dictionary<string, object?> { ["path"] = ".", ["kind"] = "make", ["arguments"] = Array.Empty<string>() });
        var host = await client.CallToolAsync("build_sync", new Dictionary<string, object?> { ["path"] = Repositories.Create("Typed"), ["host"] = "laptop" });

        Assert.Equal(Reached.Absent, gone.Type());
        Assert.Equal(Reached.Outside, elsewhere.Type());
        Assert.Equal("noSuchJob", unknown.Type());
        Assert.Equal(ToolFailures.Arguments, kind.Type());
        Assert.Equal("noSuchHost", host.Type());
    }
}
