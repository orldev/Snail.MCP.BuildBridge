namespace Snail.MCP.BuildBridge.Domain;

/// <summary>An xcodebuild command as a run shapes it: the derived data under the root, and a simulator of the run's own when the destination names one by name.</summary>
public static class XcodeCommand
{
    /// <summary>The prefix of a simulator a run clones for itself; what follows is the run's own word.</summary>
    public const string SimulatorPrefix = "snail-build-";

    /// <summary>The derived data of a workspace, relative to its root: <c>derived/&lt;repository&gt;-&lt;branch&gt;</c> under the host's root.</summary>
    public static string DerivedDataOf(WorkspaceKey key) => $"../../../derived/{WorkspaceKey.Safe(key.Repository)}-{WorkspaceKey.Safe(key.Branch)}";

    /// <summary>The arguments with the derived data under the root unless the caller named one.</summary>
    public static IReadOnlyList<string> Shaped(IReadOnlyList<string> arguments, WorkspaceKey key)
    {
        if (arguments.Contains("-derivedDataPath", StringComparer.Ordinal))
            return arguments;

        return [.. arguments, "-derivedDataPath", DerivedDataOf(key)];
    }

    /// <summary>The simulator the destination names by name, to be cloned for the run; null when the destination names none, or names a device by id.</summary>
    public static string? SimulatorNamed(IReadOnlyList<string> arguments)
    {
        var destination = DestinationOf(arguments);
        if (destination is null || !destination.GetValueOrDefault("platform", string.Empty).Contains("Simulator", StringComparison.Ordinal))
            return null;
        if (destination.ContainsKey("id"))
            return null;

        return destination.GetValueOrDefault("name");
    }

    /// <summary>The arguments with the destination pointed at the device <paramref name="udid"/> instead of a name.</summary>
    public static IReadOnlyList<string> OnDevice(IReadOnlyList<string> arguments, string udid)
    {
        int at = IndexOfDestination(arguments);
        var destination = DestinationOf(arguments)!;
        var kept = destination.Where(pair => pair.Key is not "name" and not "OS" and not "id").Select(pair => $"{pair.Key}={pair.Value}");
        string pointed = string.Join(',', kept.Append($"id={udid}"));

        return [.. arguments.Take(at + 1), pointed, .. arguments.Skip(at + 2)];
    }

    private static int IndexOfDestination(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == "-destination")
                return index;
        }

        return -1;
    }

    private static Dictionary<string, string>? DestinationOf(IReadOnlyList<string> arguments)
    {
        int at = IndexOfDestination(arguments);
        if (at < 0)
            return null;

        var pairs = arguments[at + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2);

        return pairs.ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }
}

/// <summary>A docker command as a run shapes it: every object a build makes carries the owner label, and a compose stand is a project of the run's workspace.</summary>
/// <remarks>
/// The label is the convention Snail.MCP.CleanUp reads, so a cleanup on the host sees these objects as this server's.
/// A compose stand cannot be labelled from the command line, so it is named instead, <c>s3l-&lt;repository&gt;-&lt;branch&gt;</c>,
/// unless the caller named it.
/// </remarks>
public static class DockerCommand
{
    /// <summary>The label every object a run makes carries.</summary>
    public const string Label = "snail.owner";

    /// <summary>The label's value for a workspace.</summary>
    public static string OwnerOf(WorkspaceKey key) => $"buildbridge/{WorkspaceKey.Safe(key.Repository)}/{WorkspaceKey.Safe(key.Branch)}";

    /// <summary>The compose project of a workspace.</summary>
    public static string ProjectOf(WorkspaceKey key) => $"s3l-{WorkspaceKey.Safe(key.Repository)}-{WorkspaceKey.Safe(key.Branch)}".ToLowerInvariant();

    /// <summary>The arguments with the owner label on a build or a run, and the project on a compose.</summary>
    public static IReadOnlyList<string> Shaped(IReadOnlyList<string> arguments, WorkspaceKey key)
    {
        if (arguments.Count == 0)
            return arguments;

        return arguments[0] switch
        {
            "build" or "run" or "create" => [arguments[0], "--label", $"{Label}={OwnerOf(key)}", .. arguments.Skip(1)],
            "compose" when !arguments.Contains("-p", StringComparer.Ordinal) && !arguments.Contains("--project-name", StringComparer.Ordinal) =>
                [arguments[0], "-p", ProjectOf(key), .. arguments.Skip(1)],
            _ => arguments
        };
    }
}
