using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.BuildBridge.Tools;

/// <summary>The state of the server: its version, its hosts, the programs it works through and where its data is.</summary>
[McpServerToolType]
public sealed class DiagnosticsTools(Hosts hosts, IPrograms programs, IOptions<BuildOptions> options)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Reports the server's state.</summary>
    [McpServerTool(Name = "build_diagnose", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Diagnose)]
    public async Task<CallToolResult> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(DiagnosticsTools).Assembly.GetName();
        var settings = options.Value;
        var state = new JsonObject
        {
            ["server"] = assembly.Name,
            ["version"] = assembly.Version?.ToString(3),
            ["runtime"] = Environment.Version.ToString(),
            ["hosts"] = new JsonArray([.. hosts.All.Select(host => (JsonNode)new JsonObject { ["name"] = host.Name, ["address"] = host.Address, ["root"] = host.Root })]),
            ["programs"] = new JsonObject
            {
                ["ssh"] = await VersionAsync(settings.Ssh, ["-V"], cancellationToken),
                ["rsync"] = await VersionAsync(settings.Rsync, ["--version"], cancellationToken),
                ["git"] = await VersionAsync("git", ["--version"], cancellationToken)
            },
            ["dataDirectory"] = ServerFolders.DataDirectory(BuildSettings.Name),
            ["settingsFile"] = ServerSettings.FileOf(BuildSettings.Name)
        };

        return ToolResponse.Success(state);
    }

    /// <summary>The first line a program answers its version with, or null when it is not installed.</summary>
    private async Task<string?> VersionAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var run = await programs.RunAsync(program, arguments, null, Patience, cancellationToken);
        if (run.IsMissing)
            return null;

        string said = run.Output.Length > 0 ? run.Output : run.Error;

        return said.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }
}
