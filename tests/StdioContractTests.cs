using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests;

/// <summary>The server as a program, started the way a client starts it: the build beside the tests, or the one SNAIL_BUILDBRIDGE_SERVER names.</summary>
public sealed class StdioContractTests
{
    private const string Assembly = "Snail.MCP.BuildBridge";

    private const string Variable = "SNAIL_BUILDBRIDGE_SERVER";

    [Fact]
    public async Task Tools_AsListed_KeepTheGuardRules()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        var listed = await client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    /// <summary>A client knows the server by the command a person types to start it.</summary>
    [Fact]
    public async Task Initialize_AsAProgram_NamesTheServerByItsCommand()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        Assert.Equal("s3l-mcp-buildbridge", client.ServerInfo?.Name);
    }

    [Fact]
    public async Task Diagnose_AsAProgram_NamesTheServer()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        var answer = await client.CallToolAsync("build_diagnose", new Dictionary<string, object?>());

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal(Assembly, answer.Data()!["server"]!.GetValue<string>());
    }

    /// <summary>
    /// Settings that fail their validation stop the program before it answers: Snail.Sdk turns the failure of
    /// ValidateOnStart into one line on stderr and exit code 1, not a crash with a trace and 134.
    /// </summary>
    [Fact]
    public async Task Start_WithATimeOfZero_StopsInOneLineNamingTheProblem()
    {
        var refused = await Assert.ThrowsAsync<ServerExitedException>(() =>
            ServerProgram.StartAsync(Assembly, Variable, new Dictionary<string, string?> { ["SNAIL_MCP_BUILDBRIDGE_RUN_SECONDS"] = "0" }));

        Assert.Equal(1, refused.ExitCode);
        Assert.Equal("SNAIL_MCP_BUILDBRIDGE_RUN_SECONDS (or RunSeconds in the settings file) is '0'; it is a number of seconds above zero.", Assert.Single(refused.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        Assert.DoesNotContain("   at ", refused.Stderr, StringComparison.Ordinal);
    }
}
