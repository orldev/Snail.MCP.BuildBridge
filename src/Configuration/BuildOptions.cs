using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Configuration;

/// <summary>One host as the settings name it.</summary>
public sealed class HostSetting
{
    /// <summary>The word a call names the host by.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What ssh is given: an alias from the SSH configuration, or user@host.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>The folder everything this server makes on the host lives under.</summary>
    public string Root { get; set; } = Host.DefaultRoot;

    /// <summary>The host as the domain carries it.</summary>
    public Host ToHost() => new(Name, Address, Root);
}

/// <summary>A NuGet feed of this machine mirrored to every host, so a restore there finds the same packages.</summary>
public sealed class FeedSetting
{
    /// <summary>The source's name, as the NuGet configuration of this machine and of the host both know it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The feed's folder on this machine.</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>The hosts, the programs this server works through, what a delivery leaves out, how long things may take, what a host is provisioned with, and the sweep's ages.</summary>
/// <remarks>
/// macOS 26 ships openrsync as <c>rsync</c>. It knows no filter rules, so what git ignores is listed by git and handed
/// to it as excludes; it does know excludes, deletion, itemized changes and stats, measured on 2026-11-03, which is all
/// a delivery needs, so no rsync of the server's own is carried.
/// </remarks>
public sealed class BuildOptions
{
    /// <summary>The hosts builds may run on; the first is the one a call that names none gets.</summary>
    public HostSetting[] Hosts { get; set; } = [];

    /// <summary>The ssh program.</summary>
    public string Ssh { get; set; } = "ssh";

    /// <summary>The rsync program on this machine.</summary>
    public string Rsync { get; set; } = "rsync";

    /// <summary>What carries a copy to a host: rsync, a mirror before every run, or mutagen, a session that keeps the copy mirrored as it changes.</summary>
    public string Sync { get; set; } = Delivery.Rsync;

    /// <summary>The mutagen program on this machine, used when <see cref="Sync"/> is mutagen.</summary>
    public string Mutagen { get; set; } = "mutagen";

    /// <summary>Folders a delivery never sends, wherever they are in the copy: what a build makes again.</summary>
    public string[] Excludes { get; set; } = ["bin", "obj", "artifacts", "TestResults", "BenchmarkDotNet.Artifacts", "node_modules", ".DS_Store"];

    /// <summary>Seconds a delivery or a collection may take.</summary>
    public int TransferSeconds { get; set; } = 600;

    /// <summary>Seconds a build may run.</summary>
    public int RunSeconds { get; set; } = 3600;

    /// <summary>Seconds a probe of a host may take: ssh connecting and three version commands.</summary>
    public int ProbeSeconds { get; set; } = 20;

    /// <summary>The .NET SDK provisioning installs under a host's root; empty leaves the host's own dotnet to be used.</summary>
    public string SdkVersion { get; set; } = "10.0.302";

    /// <summary>The dotnet workloads that SDK gets.</summary>
    public string[] Workloads { get; set; } = ["maui-ios", "maui-maccatalyst", "maui-android", "wasm-tools"];

    /// <summary>The NuGet feeds of this machine mirrored to every host.</summary>
    public FeedSetting[] Feeds { get; set; } = [];

    /// <summary>Whether provisioning puts XcodeGen under the root of a host that has Xcode, so a project with a project.yml is generated there before a build.</summary>
    public bool HasXcodeGen { get; set; } = true;

    /// <summary>The images provisioning builds on a host for the kinds that run in a container: dotnet for linux, android for android.</summary>
    public string[] Images { get; set; } = [BuildImage.Dotnet, BuildImage.Android];

    /// <summary>Days a workspace may go without a build before a sweep removes it.</summary>
    public int WorkspaceDays { get; set; } = 14;

    /// <summary>Days a job's log and artifacts nobody fetched are kept.</summary>
    public int JobDays { get; set; } = 30;

    /// <summary>Days an image a build made is kept.</summary>
    public int ImageDays { get; set; } = 7;

    /// <summary>Hours of Docker's build cache kept when the cache is pruned.</summary>
    public int BuildCacheHours { get; set; } = 24;

    /// <summary>Free gigabytes on a host below which a sweep runs before a build.</summary>
    public double LowDiskGigabytes { get; set; } = 20;

    /// <summary>The hosts as the domain carries them.</summary>
    public IReadOnlyList<Host> ToHosts() => [.. Hosts.Select(host => host.ToHost())];

    /// <summary>What a host is provisioned with, as the domain carries it.</summary>
    public BuilderManifest ToManifest() => new(SdkVersion, Workloads, [.. Feeds.Select(feed => feed.Name)], Images, HasXcodeGen);

    /// <summary>Whether the sync setting names a means this server knows.</summary>
    public bool HasKnownSync => Sync is Delivery.Rsync or Delivery.Mutagen;

    /// <summary>The sweep's ages as the domain carries them.</summary>
    public SweepRules ToSweepRules() => new(WorkspaceDays, JobDays, ImageDays, BuildCacheHours, LowDiskGigabytes);
}
