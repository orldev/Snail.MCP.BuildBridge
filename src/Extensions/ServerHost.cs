using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Hosting.Discovery;

namespace Snail.MCP.BuildBridge.Extensions;

/// <summary>The server as a running host, on the transport it was made for.</summary>
public static class ServerHost
{
    /// <summary>Runs the server until the client or the system ends it, or until it has heard nothing for the idle time with no job running.</summary>
    /// <remarks>The settings are read once and handed to the host and to the options alike. A client lost without closing its stdin left the process waiting for ever: three were found running on 2026-11-12, two of them 38 hours old.</remarks>
    public static Task RunAsync(string[] args)
    {
        var settings = BuildSettings.Read();
        var builder = McpHost.CreateBuilder(args, settings);
        int idleMinutes = settings.GetValue(nameof(BuildOptions.IdleTimeoutMinutes), new BuildOptions().IdleTimeoutMinutes);

        builder.Services.AddBuilders(settings);
        builder.Services.AddIdleShutdown(TimeSpan.FromMinutes(idleMinutes), services => services.GetRequiredService<Jobs>().InFlight.Count > 0);
        builder.Services.AddMcpServer(ServerRegistration.Describe).ExplainFailures().WithStdioServerTransport().WithServerTools().WithArgumentCheck();

        return builder.Build().RunAsync();
    }
}
