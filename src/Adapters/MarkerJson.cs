using System.Text.Json.Serialization;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>The generated JSON of a workspace's marker, which a native build cannot look up at run time.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WorkspaceMarker))]
public sealed partial class MarkerJson : JsonSerializerContext;
