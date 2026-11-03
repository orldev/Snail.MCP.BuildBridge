using BenchmarkDotNet.Attributes;
using ModelContextProtocol.Protocol;
using Snail.MCP.BuildBridge.Domain;
using Snail.MCP.BuildBridge.Tools;

namespace Snail.MCP.BuildBridge.Benchmarks;

/// <summary>The answers of the tools, measured; the shared perf workflow compares their allocations with the baseline.</summary>
[MemoryDiagnoser]
public class AnswerBenchmarks
{
    private static readonly HostState Mini = new(
        new Host("mini", "orldev@mini.local"),
        true,
        [new Toolchain(Capability.Dotnet, "10.0.302"), new Toolchain(Capability.Xcode, "27.1"), new Toolchain(Capability.Docker, "29.8.2")],
        612.5,
        null);

    private static readonly Delivery Delivered = new(
        new WorkspaceKey("Anonym", "feat/web-3.0"),
        [.. Enumerable.Range(1, 40).Select(number => $"src/App/File{number}.cs")],
        123456,
        TimeSpan.FromSeconds(2.4),
        false);

    /// <summary>One host as build_hosts answers it.</summary>
    [Benchmark]
    public CallToolResult Host() => ToolResponse.Success(ToolResponse.Data(Mini));

    /// <summary>One delivery of forty files as build_sync answers it.</summary>
    [Benchmark]
    public CallToolResult Sync() => ToolResponse.Success(ToolResponse.Data(Delivered));
}
