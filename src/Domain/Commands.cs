namespace Snail.MCP.BuildBridge.Domain;

/// <summary>An xcodebuild command as a run shapes it: the derived data under the root, and a simulator of the run's own when the destination names one by name.</summary>
public static class XcodeCommand
{
    /// <summary>The prefix of a simulator a run clones for itself; what follows is the run's own word.</summary>
    public const string SimulatorPrefix = "snail-build-";

    /// <summary>The derived data of a workspace, relative to the copy a command runs in: <c>derived/&lt;repository&gt;-&lt;branch&gt;</c> under the host's root, four folders up.</summary>
    public static string DerivedDataOf(WorkspaceKey key) => $"../../../../derived/{WorkspaceKey.Safe(key.Repository)}-{WorkspaceKey.Safe(key.Branch)}";

    /// <summary>The arguments with the derived data under the root unless the caller named one.</summary>
    public static IReadOnlyList<string> Shaped(IReadOnlyList<string> arguments, WorkspaceKey key)
    {
        if (arguments.Contains("-derivedDataPath", StringComparer.Ordinal))
            return arguments;

        return [.. arguments, "-derivedDataPath", DerivedDataOf(key)];
    }

    /// <summary>The simulators the destinations name by name, in their order, each to be cloned for the run; a destination that names none, or a device by id, counts for nothing.</summary>
    /// <remarks>xcodebuild takes several destinations and tests on them side by side, so a run with two of them is the way to test on an iPhone and an iPad at once: one workspace takes one run at a time.</remarks>
    public static IReadOnlyList<string> SimulatorsNamed(IReadOnlyList<string> arguments) =>
        [.. Destinations(arguments).Select(destination => SimulatorIn(destination.Fields)).OfType<string>()];

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

    /// <summary>The arguments with each destination that names a simulator pointed at the device of <paramref name="udids"/> in the same place, instead of a name.</summary>
    public static IReadOnlyList<string> OnDevices(IReadOnlyList<string> arguments, IReadOnlyList<string> udids)
    {
        var pointed = arguments.ToList();
        int next = 0;
        foreach (var (at, fields) in Destinations(arguments))
        {
            if (SimulatorIn(fields) is null || next >= udids.Count)
                continue;

            var kept = fields.Where(pair => pair.Key is not "name" and not "OS" and not "id").Select(pair => $"{pair.Key}={pair.Value}");
            pointed[at + 1] = string.Join(',', kept.Append($"id={udids[next++]}"));
        }

        return pointed;
    }

    private static string? SimulatorIn(Dictionary<string, string> destination)
    {
        if (!destination.GetValueOrDefault("platform", string.Empty).Contains("Simulator", StringComparison.Ordinal) || destination.ContainsKey("id"))
            return null;

        return destination.GetValueOrDefault("name");
    }

    private static IEnumerable<(int At, Dictionary<string, string> Fields)> Destinations(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == "-destination")
                yield return (index, FieldsOf(arguments[index + 1]));
        }
    }

    private static int IndexOf(IReadOnlyList<string> arguments, string option)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == option)
                return index;
        }

        return -1;
    }

    private static Dictionary<string, string> FieldsOf(string destination)
    {
        var pairs = destination.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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

    /// <summary>The docker arguments that run dotnet with <paramref name="arguments"/> in the kind's image, in the workspace under <paramref name="root"/>, with the run's <paramref name="variables"/> passed in by name from the shell's environment.</summary>
    public static IReadOnlyList<string> Shaped(BuildKind kind, IReadOnlyList<string> arguments, WorkspaceKey key, string root, string sdkVersion, IReadOnlyCollection<string> variables)
    {
        string image = kind.ImageOf() ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind runs on the host, not in a container.");
        List<string> platform = BuildImage.PlatformOf(image) is { } wanted ? ["--platform", wanted] : [];
        var passed = variables.SelectMany(variable => new[] { "-e", variable });

        return
        [
            "run", "--rm", "--label", $"{DockerCommand.Label}={DockerCommand.OwnerOf(key)}", .. platform,
            "-v", $"{root}/{key.CopyFolder}:{Work}", "-w", Work,
            "-v", $"{root}/nuget:/root/.nuget/packages",
            "-v", $"{root}/feeds:{Feeds}:ro",
            "-v", $"{root}/{NuGetConfig}:/root/.nuget/NuGet/NuGet.Config:ro",
            "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "-e", "DOTNET_NOLOGO=1", .. passed,
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

/// <summary>The gradle wrapper of a repository as a run starts it: the wrapper at the copy's root, with the arguments as given.</summary>
/// <remarks>The wrapper fetches the Gradle the project names, into the gradle folder under the root, where the prelude points GRADLE_USER_HOME; the toolchain comes from the root too, through JAVA_HOME and ANDROID_HOME.</remarks>
public static class GradleCommand
{
    /// <summary>The wrapper's file at the copy's root, which git keeps executable.</summary>
    public const string Wrapper = "gradlew";

    /// <summary>The wrapper as the shell is given it: from the current folder, not from the path.</summary>
    public const string Program = "./gradlew";
}
