namespace Snail.MCP.BuildBridge.Domain;

/// <summary>Which toolchain a run drives, and so which host can take it.</summary>
public enum BuildKind
{
    /// <summary>The dotnet command: build, test, publish.</summary>
    Dotnet,

    /// <summary>The xcodebuild command: build, build-for-testing, test on a simulator.</summary>
    Xcode,

    /// <summary>The docker command: build, compose.</summary>
    Docker
}

/// <summary>What <see cref="BuildKind"/> needs of a host and runs there.</summary>
public static class BuildKinds
{
    /// <summary>The capability a host must have to run a kind.</summary>
    public static Capability Needs(this BuildKind kind) => kind switch
    {
        BuildKind.Dotnet => Capability.Dotnet,
        BuildKind.Xcode => Capability.Xcode,
        BuildKind.Docker => Capability.Docker,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>The program a kind runs on the host.</summary>
    public static string Program(this BuildKind kind) => kind switch
    {
        BuildKind.Dotnet => "dotnet",
        BuildKind.Xcode => "xcodebuild",
        BuildKind.Docker => "docker",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
