namespace Snail.MCP.BuildBridge.Domain;

/// <summary>Which toolchain a run drives, and so which host can take it.</summary>
public enum BuildKind
{
    /// <summary>The dotnet command: build, test, publish.</summary>
    Dotnet,

    /// <summary>The xcodebuild command: build, build-for-testing, test on a simulator.</summary>
    Xcode,

    /// <summary>The docker command: build, compose.</summary>
    Docker,

    /// <summary>The dotnet command inside the host's Linux image: a Linux target, Native AOT for Linux, a test run on Linux.</summary>
    Linux,

    /// <summary>The dotnet command for an Android target: on the host with the Android toolchain under its root, or inside the host's Android image where the toolchain is not there.</summary>
    Android,

    /// <summary>The repository's gradle wrapper: an Android app of its own, built with the toolchain under the root.</summary>
    Gradle,

    /// <summary>A script of the repository, run as it is: what a build needs that no one program says, such as packing a core into a framework.</summary>
    Script
}

/// <summary>What <see cref="BuildKind"/> needs of a host and runs there.</summary>
public static class BuildKinds
{
    /// <summary>The capabilities any one of which lets a host take a kind, the first preferred; none for a script, which asks for whatever it asks for.</summary>
    public static IReadOnlyList<Capability> Needs(this BuildKind kind) => kind switch
    {
        BuildKind.Dotnet => [Capability.Dotnet],
        BuildKind.Xcode => [Capability.Xcode],
        BuildKind.Docker or BuildKind.Linux => [Capability.Docker],
        BuildKind.Android => [Capability.Android, Capability.Docker],
        BuildKind.Gradle => [Capability.Android],
        BuildKind.Script => [],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>The program a kind runs on the host, or docker, which runs dotnet, where it runs in <paramref name="image"/>; null for a script, which is its own program.</summary>
    public static string? Program(this BuildKind kind, string? image) => image is not null ? "docker" : kind switch
    {
        BuildKind.Dotnet or BuildKind.Android => "dotnet",
        BuildKind.Xcode => "xcodebuild",
        BuildKind.Docker => "docker",
        BuildKind.Gradle => GradleCommand.Program,
        BuildKind.Linux => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind runs in a container, not on the host."),
        BuildKind.Script => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>The image a kind runs in when it runs in a container, by the name provisioning builds it under; null for a kind that only runs on the host.</summary>
    public static string? ImageOf(this BuildKind kind) => kind switch
    {
        BuildKind.Linux => BuildImage.Dotnet,
        BuildKind.Android => BuildImage.Android,
        _ => null
    };

    /// <summary>The image a kind runs in on a host as it stands; null where it runs in the host's own folders.</summary>
    /// <remarks>A folder is cheaper than a container, so a container is paid for only where the host cannot do the work itself: a Linux target always, an Android build only on a host without the toolchain under its root.</remarks>
    public static string? ImageOn(this BuildKind kind, HostState state) => kind switch
    {
        BuildKind.Android when state.Has(Capability.Android) => null,
        _ => kind.ImageOf()
    };
}
