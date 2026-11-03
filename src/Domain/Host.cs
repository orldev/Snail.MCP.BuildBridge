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

    /// <summary>The root as an absolute path on the host, given the home folder the host's shell reported.</summary>
    public string RootOn(string home) => Root.StartsWith("~/", StringComparison.Ordinal) ? $"{home}/{Root[2..]}" : Root;
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

/// <summary>A host as a probe saw it: whether it answered, what it builds with, how much room it has, and where its home is.</summary>
/// <param name="Host">The host probed.</param>
/// <param name="IsReachable">Whether it answered over ssh.</param>
/// <param name="Toolchains">What it builds with, present or absent.</param>
/// <param name="FreeGigabytes">The room under its home; null when it did not answer.</param>
/// <param name="Note">Why it did not answer, when it did not.</param>
/// <param name="Home">The home folder of the account on the host, which a container run needs to name the root to docker; null when the probe did not say.</param>
public sealed record HostState(Host Host, bool IsReachable, IReadOnlyList<Toolchain> Toolchains, double? FreeGigabytes, string? Note, string? Home = null)
{
    /// <summary>Whether the host builds with <paramref name="capability"/>.</summary>
    public bool Has(Capability capability) => Toolchains.Any(toolchain => toolchain.Capability == capability && toolchain.IsPresent);

    /// <summary>A host that did not answer, and why.</summary>
    public static HostState Unreachable(Host host, string note) => new(host, false, [], null, note);
}
