namespace Snail.MCP.BuildBridge.Application;

/// <summary>A value, or why there is none in words a caller can act on.</summary>
public sealed record Answer<T>(T? Value, string? Error, string? Hint);

/// <summary>Makes answers.</summary>
public static class Answer
{
    /// <summary>The value.</summary>
    public static Answer<T> Ok<T>(T value) => new(value, null, null);

    /// <summary>No value, and why.</summary>
    public static Answer<T> Failed<T>(string error, string? hint = null) => new(default, error, hint);
}
