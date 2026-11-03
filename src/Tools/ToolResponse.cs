using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.BuildBridge.Tools;

/// <summary>Answers as tool results: JSON with an ok flag, the data on success, error and hint otherwise.</summary>
/// <remarks>The structured content is written through the generated context: a native build has no reflection to serialize with.</remarks>
public static class ToolResponse
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>A result carrying the data.</summary>
    public static CallToolResult Success(JsonObject data) => new()
    {
        Content = [new TextContentBlock { Text = new JsonObject { ["ok"] = true, ["data"] = data.DeepClone() }.ToJsonString(Options) }],
        StructuredContent = JsonSerializer.SerializeToElement(data, AnswerJson.Default.JsonObject)
    };

    /// <summary>A result saying why nothing happened.</summary>
    public static CallToolResult Failure(string error, string? hint) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = new JsonObject { ["ok"] = false, ["error"] = error, ["hint"] = hint }.ToJsonString(Options) }],
        StructuredContent = JsonSerializer.SerializeToElement(new JsonObject { ["error"] = error, ["hint"] = hint }, AnswerJson.Default.JsonObject)
    };

    /// <summary>An answer as a result: its data through <paramref name="data"/>, or its error.</summary>
    public static CallToolResult From<T>(Answer<T> answer, Func<T, JsonObject> data) =>
        answer.Value is { } value ? Success(data(value)) : Failure(answer.Error ?? "Nothing was answered.", answer.Hint);

    /// <summary>An enum value as the answer spells it.</summary>
    public static string Word<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    /// <summary>A host as an answer carries it: whether it answers, its toolchains with versions, and its room.</summary>
    public static JsonObject Data(HostState state) => new()
    {
        ["name"] = state.Host.Name,
        ["address"] = state.Host.Address,
        ["root"] = state.Host.Root,
        ["reachable"] = state.IsReachable,
        ["toolchains"] = new JsonObject([.. state.Toolchains.Select(toolchain => KeyValuePair.Create(Word(toolchain.Capability), (JsonNode?)toolchain.Version))]),
        ["freeGigabytes"] = state.FreeGigabytes,
        ["note"] = state.Note
    };

    /// <summary>A delivery as an answer carries it: the workspace, what moved and how much.</summary>
    public static JsonObject Data(Delivery delivery) => new()
    {
        ["repository"] = delivery.Key.Repository,
        ["branch"] = delivery.Key.Branch,
        ["workspace"] = delivery.Key.Folder,
        ["dryRun"] = delivery.IsDryRun,
        ["files"] = new JsonArray([.. delivery.Files.Select(file => JsonValue.Create(file))]),
        ["bytes"] = delivery.Bytes,
        ["seconds"] = Math.Round(delivery.Took.TotalSeconds, 1)
    };

    /// <summary>A provisioned host as an answer carries it: each step, what is left to do by hand, and where the log is.</summary>
    public static JsonObject Data(Provisioned provisioned) => new()
    {
        ["host"] = provisioned.Host.Name,
        ["complete"] = provisioned.IsComplete,
        ["steps"] = new JsonArray([.. provisioned.Steps.Select(step => (JsonNode)new JsonObject { ["name"] = step.Name, ["done"] = step.IsDone, ["note"] = step.Note })]),
        ["byHand"] = new JsonArray([.. provisioned.ByHand.Select(step => JsonValue.Create($"{step.Name}: {step.Note}"))]),
        ["log"] = provisioned.Log
    };

    /// <summary>A build as an answer carries it: whether it succeeded, how the command ended, what the tests said, and where the log is.</summary>
    public static JsonObject Data(BuildReport report) => new()
    {
        ["success"] = report.IsSuccess,
        ["host"] = report.Host.Name,
        ["kind"] = Word(report.Kind),
        ["command"] = string.Join(' ', report.Command),
        ["workspace"] = report.Delivery.Key.Folder,
        ["delivered"] = new JsonObject { ["files"] = report.Delivery.Files.Count, ["bytes"] = report.Delivery.Bytes, ["seconds"] = Math.Round(report.Delivery.Took.TotalSeconds, 1) },
        ["exitCode"] = report.Execution.ExitCode,
        ["timedOut"] = report.Execution.IsTimedOut,
        ["seconds"] = Math.Round(report.Execution.Took.TotalSeconds, 1),
        ["tests"] = report.Tests is { } tests ? new JsonObject { ["passed"] = tests.Passed, ["failed"] = tests.Failed, ["skipped"] = tests.Skipped, ["total"] = tests.Total } : null,
        ["log"] = report.Execution.Log,
        ["swept"] = new JsonArray([.. report.Swept.Select(Data)])
    };

    /// <summary>A plan as an answer carries it: its id to carry it out by, each item with why, and what it knows it gives back.</summary>
    public static JsonObject Data(SweepPlan plan) => new()
    {
        ["planId"] = plan.Id,
        ["host"] = plan.Host.Name,
        ["scope"] = Word(plan.Scope),
        ["items"] = new JsonArray([.. plan.Items.Select(Data)]),
        ["knownBytes"] = plan.KnownBytes,
        ["keptUntil"] = Moment(plan.MadeAt + SweepPlan.Keeps)
    };

    /// <summary>A sweep's results as an answer carries them.</summary>
    public static JsonObject Data(IReadOnlyList<SweepResult> results) => new()
    {
        ["done"] = results.Count(result => result.IsDone),
        ["failed"] = results.Count(result => !result.IsDone),
        ["items"] = new JsonArray([.. results.Select(Data)])
    };

    private static JsonNode Data(SweepItem item) => new JsonObject
    {
        ["kind"] = Word(item.Kind),
        ["name"] = item.Name,
        ["bytes"] = item.Bytes,
        ["reason"] = item.Reason
    };

    private static JsonNode Data(SweepResult result) => new JsonObject
    {
        ["kind"] = Word(result.Item.Kind),
        ["name"] = result.Item.Name,
        ["done"] = result.IsDone,
        ["note"] = result.Note
    };

    /// <summary>Paths brought back as an answer carries them.</summary>
    public static JsonObject Data(Collected collected) => new()
    {
        ["folder"] = collected.Folder,
        ["files"] = new JsonArray([.. collected.Files.Select(file => JsonValue.Create(file))]),
        ["bytes"] = collected.Bytes
    };

    /// <summary>A job as a tool result: its report, with the work's answer as its result once it ended.</summary>
    public static CallToolResult Job(JobReport report) => Success(JsonNode.Parse(report.ToJson())!.AsObject());

    /// <summary>A moment as an answer spells it.</summary>
    public static string Moment(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);
}
