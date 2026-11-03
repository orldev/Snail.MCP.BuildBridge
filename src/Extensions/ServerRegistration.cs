using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tools;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.BuildBridge.Extensions;

/// <summary>What the server says about itself and which tools it offers, the same over every transport.</summary>
public static class ServerRegistration
{
    /// <summary>The words a client shows the model before any tool: what the server is for and where to start.</summary>
    public const string Instructions =
        """
        Builds, tests and Docker work of a working copy on another Mac over ssh, with the results brought back: this
        machine stays the place code is written, the host does the heavy work. build_hosts says which hosts there are
        and what each can do; build_provision makes a new host ready; build_run delivers a copy and runs dotnet,
        xcodebuild or docker in it as a job; build_logs reads a run's log; build_fetch brings outputs back;
        build_cancel stops a job; build_sync shows or makes a delivery on its own; build_clean gives a host its room
        back, a plan first; build_diagnose says what state this server is in. Start with build_hosts.
        """;

    /// <summary>The server's name, version and instructions.</summary>
    public static void Describe(McpServerOptions server)
    {
        server.ServerInfo = ServerIdentity.Of(typeof(ServerRegistration).Assembly);
        server.ServerInstructions = Instructions;
    }

    extension(IMcpServerBuilder builder)
    {
        /// <summary>Every tool of the server; a native build keeps the methods of the classes named here.</summary>
        public IMcpServerBuilder WithServerTools() => builder.WithTools<BuildTools>().WithTools<DiagnosticsTools>().WithJobs(BuildSettings.Name);
    }
}
