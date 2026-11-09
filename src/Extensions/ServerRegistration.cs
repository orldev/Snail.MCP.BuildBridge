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
        and what each can do — dotnet, Xcode, Docker, the Android toolchain, Node, the SDKs it holds; build_provision
        makes a new host ready and names what a person does by hand; build_run delivers a copy and runs dotnet,
        xcodebuild, docker, npm, the repository's gradlew or a script of its own in it as a job; build_logs reads a run's log;
        build_fetch brings outputs back; build_cancel stops a job; build_sync shows or makes a delivery on its own;
        build_clean gives a host its room back, a plan first; build_diagnose says what state this server is in.

        What a build needs comes with the call, not from the settings: name Android SDK packages in needs, the .NET
        SDK in the copy's global.json, variables that are no secret in environment, and a build that lacks a platform
        or workloads gets them and runs again by itself; secrets are the settings' alone, and reach a run from this
        machine without passing through a tool. One workspace — a repository on a branch — takes one run at a time; runs in other workspaces go side
        by side, and several simulators are several -destination in one xcode run, not several runs. Start with
        build_hosts.

        Every answer is JSON with an ok flag, and a failed call is also flagged isError. When ok is false, read error
        and hint and act on them instead of repeating the call as it was; type names the kind of failure, a word to
        branch on: arguments, outside, noPath, absent, timeout, noHosts, noSuchHost, noSuchJob, noSuchPlan,
        unreachable, noToolchain, notACopy, notInstalled, noSecret, noHome, notDelivered, notCollected,
        notProvisioned, notSupplied, noSimulator, notGenerated, notSurveyed. A job that could not happen ends failed
        with the type of what stopped it. A build that ran and failed is not such a failure: its job succeeded, and its result says
        success false with the exit code and the log.
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
