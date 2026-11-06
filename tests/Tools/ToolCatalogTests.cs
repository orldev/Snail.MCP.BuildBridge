using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tools;

namespace Snail.MCP.BuildBridge.Tests.Tools;

/// <summary>The descriptions a model reads, held to the code they describe.</summary>
public sealed partial class ToolCatalogTests
{
    private static readonly BuildOptions Defaults = new();

    private static IEnumerable<string> Tools()
    {
        var methods = typeof(BuildTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods());

        return methods.Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name).OfType<string>();
    }

    [Fact]
    public void Tools_EveryOne_IsNamedForThisServer()
    {
        Assert.Equal(["build_cancel", "build_clean", "build_diagnose", "build_fetch", "build_hosts", "build_logs", "build_provision", "build_run", "build_sync"], Tools().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Capabilities_EveryCapability_IsNamed()
    {
        foreach (var capability in Enum.GetValues<Capability>())
            Assert.Contains(ToolResponse.Word(capability), Words(ToolDescriptions.Capabilities));
    }

    [Fact]
    public void Kinds_EveryKind_IsNamed()
    {
        foreach (var kind in Enum.GetValues<BuildKind>())
            Assert.Contains(ToolResponse.Word(kind), Words(ToolDescriptions.Kinds));
    }

    [Fact]
    public void Scopes_EveryScope_IsNamed()
    {
        foreach (var scope in Enum.GetValues<SweepScope>())
            Assert.Contains(ToolResponse.Word(scope), Words(ToolDescriptions.Scopes));
    }

    [Fact]
    public void Clean_MinutesItNames_AreHowLongAPlanKeeps()
    {
        Assert.Contains($"{SweepPlan.Keeps.TotalMinutes}", Number().Matches(ToolDescriptions.Clean).Select(match => match.Value));
    }

    [Fact]
    public void Logs_LinesItNames_AreTheTool()
    {
        var numbers = Number().Matches(ToolDescriptions.Logs).Select(match => match.Value).ToList();

        Assert.Contains($"{BuildTools.LogLines}", numbers);
        Assert.Contains($"{BuildTools.MaxLogLines}", numbers);
    }

    /// <summary>Every number a description names is the default it describes.</summary>
    [Theory]
    [InlineData(ToolDescriptions.Sync, nameof(BuildOptions.TransferSeconds))]
    [InlineData(ToolDescriptions.Provision, nameof(BuildOptions.RunSeconds))]
    [InlineData(ToolDescriptions.Run, nameof(BuildOptions.RunSeconds))]
    [InlineData(ToolDescriptions.Clean, nameof(BuildOptions.WorkspaceDays))]
    [InlineData(ToolDescriptions.Clean, nameof(BuildOptions.ImageDays))]
    [InlineData(ToolDescriptions.Clean, nameof(BuildOptions.BuildCacheHours))]
    [InlineData(ToolDescriptions.Clean, nameof(BuildOptions.JobDays))]
    [InlineData(ToolDescriptions.Clean, nameof(BuildOptions.LowDiskGigabytes))]
    [InlineData(ToolDescriptions.Clean, nameof(BuildOptions.OutputDays))]
    public void Description_NumberItNames_IsTheDefault(string description, string option)
    {
        object? value = typeof(BuildOptions).GetProperty(option)!.GetValue(Defaults);

        Assert.Contains($"{value}", Number().Matches(description).Select(match => match.Value));
    }

    /// <summary>Every folder a delivery is said to leave out is one the settings leave out.</summary>
    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("artifacts")]
    [InlineData("TestResults")]
    public void Sync_FolderItNames_IsExcluded(string folder)
    {
        Assert.Contains(folder, Words(ToolDescriptions.Sync));
        Assert.Contains(folder, Defaults.Excludes);
    }

    private static IReadOnlyList<string> Words(string text) => [.. text.Split([' ', ',', '.', '\n', '—'], StringSplitOptions.RemoveEmptyEntries)];

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}
