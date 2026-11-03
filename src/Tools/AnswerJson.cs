using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Snail.MCP.BuildBridge.Tools;

/// <summary>The generated JSON of what a tool answers with, which a native build cannot look up at run time.</summary>
[JsonSerializable(typeof(JsonObject))]
public sealed partial class AnswerJson : JsonSerializerContext;
