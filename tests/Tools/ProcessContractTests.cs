using System.Text.Json;
using Snail.MCP.BuildBridge.Tests.Support;
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

    private static JsonElement Data(string text) => JsonDocument.Parse(text).RootElement.GetProperty("data");

    [Fact]
    public async Task Hosts_ThisMachine_AnswersReachableWithDotnet()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable, ThisMachine(TestFolder.Create()));

        var answer = await client.CallToolAsync("build_hosts", new Dictionary<string, object?>());

        var host = Data(answer.Text()).GetProperty("hosts")[0];
        Assert.True(host.GetProperty("reachable").GetBoolean());
        Assert.Equal(JsonValueKind.String, host.GetProperty("toolchains").GetProperty("dotnet").ValueKind);
    }

    [Fact]
    public async Task Run_DotnetVersionInACopy_EndsSucceededWithItsLogReadableAndItsWorkspaceSweepable()
    {
        string root = TestFolder.Create();
        string repository = Repositories.Create("Contract");
        await using var client = await ServerProgram.StartAsync(Assembly, Variable, ThisMachine(root));

        var ran = await client.CallToolAsync("build_run", new Dictionary<string, object?> { ["path"] = repository, ["kind"] = "dotnet", ["arguments"] = new[] { "--version" }, ["wait"] = 120 });
        var job = Data(ran.Text());
        var result = job.GetProperty("result");
        var read = await client.CallToolAsync("build_logs", new Dictionary<string, object?> { ["log"] = result.GetProperty("log").GetString() });
        var planned = await client.CallToolAsync("build_clean", new Dictionary<string, object?> { ["scope"] = "workspace", ["path"] = repository });
        string planId = Data(planned.Text()).GetProperty("planId").GetString()!;
        var applied = await client.CallToolAsync("build_clean", new Dictionary<string, object?> { ["scope"] = "workspace", ["planId"] = planId });

        Assert.Equal("succeeded", job.GetProperty("state").GetString());
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Contains("10.", Data(read.Text()).GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("Contract/main", Data(planned.Text()).GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(1, Data(applied.Text()).GetProperty("done").GetInt32());
        Assert.False(Directory.Exists(Path.Combine(root, "workspaces", "Contract", "main")));
    }
}
