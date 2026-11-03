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

    /// <summary>The dotnet command inside the host's Android image: an Android build with the SDK and the JDK the image carries.</summary>
    Android,

    /// <summary>A script of the repository, run as it is: what a build needs that no one program says, such as packing a core into a framework.</summary>
    Script
}

/// <summary>What <see cref="BuildKind"/> needs of a host and runs there.</summary>
public static class BuildKinds
{
    /// <summary>The capability a host must have to run a kind; null for a script, which asks for whatever it asks for.</summary>
    public static Capability? Needs(this BuildKind kind) => kind switch
    {
        BuildKind.Dotnet => Capability.Dotnet,
        BuildKind.Xcode => Capability.Xcode,
        BuildKind.Docker or BuildKind.Linux or BuildKind.Android => Capability.Docker,
        BuildKind.Script => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>The program a kind runs on the host; a kind that runs in a container runs docker, which runs dotnet; null for a script, which is its own program.</summary>
    public static string? Program(this BuildKind kind) => kind switch
    {
        BuildKind.Dotnet => "dotnet",
        BuildKind.Xcode => "xcodebuild",
        BuildKind.Docker or BuildKind.Linux or BuildKind.Android => "docker",
        BuildKind.Script => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>The image a kind runs in, by the name provisioning builds it under; null for a kind that runs on the host.</summary>
    public static string? ImageOf(this BuildKind kind) => kind switch
    {
        BuildKind.Linux => BuildImage.Dotnet,
        BuildKind.Android => BuildImage.Android,
        _ => null
    };
}
