using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Discovery;

namespace Snail.MCP.BuildBridge.Extensions;

/// <summary>The server as a running host, on the transport it was made for.</summary>
public static class ServerHost
{
    /// <summary>Runs the server until the client or the system ends it.</summary>
    public static Task RunAsync(string[] args)
    {
        var builder = McpHost.CreateBuilder(args);

        builder.Services.AddBuilders();
        builder.Services.AddMcpServer(ServerRegistration.Describe).ExplainFailures().WithStdioServerTransport().WithServerTools().WithArgumentCheck();

        return builder.Build().RunAsync();
    }
}
