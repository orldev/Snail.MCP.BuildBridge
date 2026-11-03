namespace Snail.MCP.BuildBridge.Domain;

/// <summary>A machine builds run on: how it is reached over SSH and where it keeps everything this server makes.</summary>
/// <param name="Name">The word a call names it by, such as mini.</param>
/// <param name="Address">What ssh is given: a host alias from the SSH configuration, or user@host.</param>
/// <param name="Root">The folder under which every workspace, toolchain and log lives, as the host's shell reads it.</param>
public sealed record Host(string Name, string Address, string Root = Host.DefaultRoot)
{
    /// <summary>The root every host gets unless its settings say otherwise.</summary>
    public const string DefaultRoot = "~/.s3l-build";

    /// <summary>Whether the address is this very machine, which the tests use as a builder of their own.</summary>
    public bool IsLocal => Address is "localhost" or "127.0.0.1" or "::1";
}

/// <summary>What a host can build with.</summary>
public enum Capability
{
    /// <summary>The .NET SDK: dotnet build, test and publish.</summary>
    Dotnet,

    /// <summary>Xcode: xcodebuild, the simulators and their tests.</summary>
    Xcode,

    /// <summary>A Docker engine: images and compose stands.</summary>
    Docker
}

/// <summary>One toolchain as a probe found it: present with its version, or absent.</summary>
public sealed record Toolchain(Capability Capability, string? Version)
{
    /// <summary>Whether the toolchain is there to build with.</summary>
    public bool IsPresent => Version is not null;
}

/// <summary>A host as a probe saw it: whether it answered, what it builds with, and how much room it has.</summary>
public sealed record HostState(Host Host, bool IsReachable, IReadOnlyList<Toolchain> Toolchains, double? FreeGigabytes, string? Note)
{
    /// <summary>Whether the host builds with <paramref name="capability"/>.</summary>
    public bool Has(Capability capability) => Toolchains.Any(toolchain => toolchain.Capability == capability && toolchain.IsPresent);

    /// <summary>A host that did not answer, and why.</summary>
    public static HostState Unreachable(Host host, string note) => new(host, false, [], null, note);
}
