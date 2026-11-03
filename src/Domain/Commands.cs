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

    /// <summary>The spec XcodeGen reads, beside the project the arguments name — or at the workspace's root when they name none.</summary>
    public const string Spec = "project.yml";

    /// <summary>The folder the project the arguments name lies in, relative to the workspace: where a spec would be.</summary>
    public static string ProjectFolderOf(IReadOnlyList<string> arguments)
    {
        int at = IndexOf(arguments, "-project");
        if (at < 0)
            return ".";

        string folder = Path.GetDirectoryName(arguments[at + 1]) ?? string.Empty;

        return folder.Length > 0 ? folder : ".";
    }

    /// <summary>The command that generates the project from its spec in <paramref name="folder"/>, run in the workspace before xcodebuild.</summary>
    public static IReadOnlyList<string> Generation(string folder) =>
        ["xcodegen", "generate", "--spec", folder == "." ? Spec : $"{folder}/{Spec}", "--project", folder];

    /// <summary>The arguments with the destination pointed at the device <paramref name="udid"/> instead of a name.</summary>
    public static IReadOnlyList<string> OnDevice(IReadOnlyList<string> arguments, string udid)
    {
        int at = IndexOfDestination(arguments);
        var destination = DestinationOf(arguments)!;
        var kept = destination.Where(pair => pair.Key is not "name" and not "OS" and not "id").Select(pair => $"{pair.Key}={pair.Value}");
        string pointed = string.Join(',', kept.Append($"id={udid}"));

        return [.. arguments.Take(at + 1), pointed, .. arguments.Skip(at + 2)];
    }

    private static int IndexOfDestination(IReadOnlyList<string> arguments) => IndexOf(arguments, "-destination");

    private static int IndexOf(IReadOnlyList<string> arguments, string option)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == option)
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

/// <summary>A dotnet command as a run shapes it for a container: docker runs the kind's image with the workspace, the packages and the feeds mounted, and dotnet inside it.</summary>
/// <remarks>
/// The workspace is mounted where it is on the host, so the container writes <c>bin</c> and <c>obj</c> where a run on
/// the host would; the packages folder is shared with the host's dotnet, since a package is the same on every
/// platform; the feeds are mounted read-only, and a NuGet configuration provisioning wrote under <c>containers</c> names
/// them, so a restore inside finds what a restore on the host finds.
/// </remarks>
public static class ContainerCommand
{
    /// <summary>Where the workspace is mounted inside the container.</summary>
    public const string Work = "/work";

    /// <summary>Where the mirrored feeds are mounted inside the container.</summary>
    public const string Feeds = "/feeds";

    /// <summary>The NuGet configuration provisioning writes for containers, relative to the root.</summary>
    public const string NuGetConfig = "containers/NuGet.Config";

    /// <summary>The docker arguments that run dotnet with <paramref name="arguments"/> in the kind's image, in the workspace under <paramref name="root"/>.</summary>
    public static IReadOnlyList<string> Shaped(BuildKind kind, IReadOnlyList<string> arguments, WorkspaceKey key, string root, string sdkVersion)
    {
        string image = kind.ImageOf() ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind runs on the host, not in a container.");
        List<string> platform = BuildImage.PlatformOf(image) is { } wanted ? ["--platform", wanted] : [];

        return
        [
            "run", "--rm", "--label", $"{DockerCommand.Label}={DockerCommand.OwnerOf(key)}", .. platform,
            "-v", $"{root}/{key.Folder}:{Work}", "-w", Work,
            "-v", $"{root}/nuget:/root/.nuget/packages",
            "-v", $"{root}/feeds:{Feeds}:ro",
            "-v", $"{root}/{NuGetConfig}:/root/.nuget/NuGet/NuGet.Config:ro",
            "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "-e", "DOTNET_NOLOGO=1",
            BuildImage.TagOf(image, sdkVersion), "dotnet", .. arguments
        ];
    }
}

/// <summary>A script of the repository as a run starts it: the path the first argument names, from the workspace's root, with the rest as its arguments.</summary>
/// <remarks>
/// The script runs as it is, through its own shebang, so it must be executable — git keeps the bit, and so do rsync and
/// Mutagen. A path without a slash is spoken of as <c>./name</c>, or the shell would look for it on the path instead.
/// </remarks>
public static class ScriptCommand
{
    /// <summary>Whether a path stays inside the workspace: relative, and never climbing out.</summary>
    public static bool IsInside(string path) =>
        path.Length > 0 && !Path.IsPathRooted(path) && !path.Split('/').Contains("..");

    /// <summary>The script's path as the first argument names it, or null when there is none.</summary>
    public static string? PathOf(IReadOnlyList<string> arguments) => arguments.Count > 0 ? arguments[0] : null;

    /// <summary>The command that runs the script: its path as the shell takes it, then its arguments.</summary>
    public static IReadOnlyList<string> Shaped(IReadOnlyList<string> arguments)
    {
        string path = arguments[0];
        string program = path.Contains('/') ? path : $"./{path}";

        return [program, .. arguments.Skip(1)];
    }
}
