using System.Text.Json.Nodes;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Tools;

/// <summary>What the tools answer with, as the data of the envelope <see cref="ToolAnswer"/> writes.</summary>
public static class AnswerFields
{
    /// <summary>A host as an answer carries it: whether it answers, its toolchains with versions, and its room.</summary>
    public static JsonObject Of(HostState state) => new()
    {
        ["name"] = state.Host.Name,
        ["address"] = state.Host.Address,
        ["root"] = state.Host.Root,
        ["reachable"] = state.IsReachable,
        ["toolchains"] = new JsonObject([.. state.Toolchains.Select(toolchain => KeyValuePair.Create(ToolAnswer.Word(toolchain.Capability), (JsonNode?)toolchain.Version))]),
        ["sdks"] = new JsonArray([.. state.Sdks.Select(sdk => JsonValue.Create(sdk))]),
        ["freeGigabytes"] = state.FreeGigabytes,
        ["note"] = state.Note
    };

    /// <summary>A delivery as an answer carries it: the workspace, what moved and how much.</summary>
    public static JsonObject Of(Delivery delivery) => new()
    {
        ["repository"] = delivery.Key.Repository,
        ["branch"] = delivery.Key.Branch,
        ["workspace"] = delivery.Key.CopyFolder,
        ["dryRun"] = delivery.IsDryRun,
        ["means"] = delivery.Means,
        ["files"] = new JsonArray([.. delivery.Files.Select(file => JsonValue.Create(file))]),
        ["bytes"] = delivery.Bytes,
        ["seconds"] = Math.Round(delivery.Took.TotalSeconds, 1)
    };

    /// <summary>A provisioned host as an answer carries it: each step, what is left to do by hand, and where the log is.</summary>
    public static JsonObject Of(Provisioned provisioned) => new()
    {
        ["host"] = provisioned.Host.Name,
        ["complete"] = provisioned.IsComplete,
        ["steps"] = new JsonArray([.. provisioned.Steps.Select(step => (JsonNode)new JsonObject { ["name"] = step.Name, ["done"] = step.IsDone, ["note"] = step.Note })]),
        ["byHand"] = new JsonArray([.. provisioned.ByHand.Select(step => JsonValue.Create($"{step.Name}: {step.Note}"))]),
        ["log"] = provisioned.Log
    };

    /// <summary>A build as an answer carries it: whether it succeeded, how the command ended, what the tests said, and where the log is.</summary>
    public static JsonObject Of(BuildReport report) => new()
    {
        ["success"] = report.IsSuccess,
        ["host"] = report.Host.Name,
        ["kind"] = ToolAnswer.Word(report.Kind),
        ["command"] = string.Join(' ', report.Command),
        ["workspace"] = report.Delivery.Key.CopyFolder,
        ["delivered"] = new JsonObject { ["files"] = report.Delivery.Files.Count, ["bytes"] = report.Delivery.Bytes, ["seconds"] = Math.Round(report.Delivery.Took.TotalSeconds, 1) },
        ["exitCode"] = report.Execution.ExitCode,
        ["timedOut"] = report.Execution.IsTimedOut,
        ["seconds"] = Math.Round(report.Execution.Took.TotalSeconds, 1),
        ["tests"] = report.Tests is { } tests ? new JsonObject { ["passed"] = tests.Passed, ["failed"] = tests.Failed, ["skipped"] = tests.Skipped, ["total"] = tests.Total } : null,
        ["log"] = report.Execution.Log,
        ["supplied"] = new JsonArray([.. report.Supplied.Select(package => JsonValue.Create(package))]),
        ["swept"] = new JsonArray([.. report.Swept.Select(Of)])
    };

    /// <summary>A plan as an answer carries it: its id to carry it out by, each item with why, and what it knows it gives back.</summary>
    public static JsonObject Of(SweepPlan plan) => new()
    {
        ["planId"] = plan.Id,
        ["host"] = plan.Host.Name,
        ["scope"] = ToolAnswer.Word(plan.Scope),
        ["items"] = new JsonArray([.. plan.Items.Select(Of)]),
        ["knownBytes"] = plan.KnownBytes,
        ["keptUntil"] = ToolAnswer.Moment(plan.MadeAt + SweepPlan.Keeps)
    };

    /// <summary>A sweep's results as an answer carries them.</summary>
    public static JsonObject Of(IReadOnlyList<SweepResult> results) => new()
    {
        ["done"] = results.Count(result => result.IsDone),
        ["failed"] = results.Count(result => !result.IsDone),
        ["items"] = new JsonArray([.. results.Select(Of)])
    };

    private static JsonNode Of(SweepItem item) => new JsonObject
    {
        ["kind"] = ToolAnswer.Word(item.Kind),
        ["name"] = item.Name,
        ["bytes"] = item.Bytes,
        ["reason"] = item.Reason
    };

    private static JsonNode Of(SweepResult result) => new JsonObject
    {
        ["kind"] = ToolAnswer.Word(result.Item.Kind),
        ["name"] = result.Item.Name,
        ["done"] = result.IsDone,
        ["note"] = result.Note
    };

    /// <summary>Paths brought back as an answer carries them.</summary>
    public static JsonObject Of(Collected collected) => new()
    {
        ["folder"] = collected.Folder,
        ["files"] = new JsonArray([.. collected.Files.Select(file => JsonValue.Create(file))]),
        ["bytes"] = collected.Bytes
    };
}
