namespace Snail.MCP.BuildBridge.Domain;

/// <summary>How old something on a host must be before a sweep removes it, and how little room makes a sweep run first.</summary>
/// <remarks>
/// Nothing on a host is the only copy of anything — a workspace is a copy of this machine's, outputs and caches rebuild
/// themselves, a job's log was already answered — so a sweep erases rather than keeping a bin. The ages are the owner's
/// decision of 2026-11-03: a branch not built for two weeks is done with; a job's log is read within a month or never.
/// A branch built every day is never done with, so what its builds leave inside its copy goes by an age of its own: an iOS
/// repository kept 13 GB of derived data and 12 GB of test bundles in its artifacts on 2026-11-06, and the build cache of
/// one day was 10.8 GB — which is why six hours of it are kept, and why it is pruned after every run (2026-11-06).
/// </remarks>
/// <param name="WorkspaceDays">Days a workspace may go without a build before it is removed.</param>
/// <param name="JobDays">Days a job's log and artifacts nobody fetched are kept.</param>
/// <param name="ImageDays">Days an image a build made is kept.</param>
/// <param name="BuildCacheHours">Hours of Docker's build cache that are kept when the cache is pruned.</param>
/// <param name="LowDiskGigabytes">Free gigabytes below which a sweep runs before a build does.</param>
/// <param name="OutputDays">Days Xcode's derived data or a test bundle inside a workspace's copy may go unwritten before it is removed.</param>
public sealed record SweepRules(int WorkspaceDays = 14, int JobDays = 30, int ImageDays = 7, int BuildCacheHours = 6, double LowDiskGigabytes = 20, int OutputDays = 7)
{
    /// <summary>The SDK version the settings name, which a sweep of old versions keeps.</summary>
    public string KeptSdk { get; init; } = string.Empty;

    /// <summary>The Android SDK packages the settings name, which a sweep of old versions keeps.</summary>
    public IReadOnlyList<string> KeptPackages { get; init; } = [];

    /// <summary>The tags of the toolchain images the settings name, which a sweep of old versions keeps.</summary>
    public IReadOnlyList<string> KeptImages { get; init; } = [];

    /// <summary>Whether a workspace built or delivered last at <paramref name="lastUsed"/> is done with at <paramref name="now"/>.</summary>
    public bool IsStale(DateTimeOffset lastUsed, DateTimeOffset now) => now - lastUsed > TimeSpan.FromDays(WorkspaceDays);

    /// <summary>Whether an output last written at <paramref name="written"/> is done with at <paramref name="now"/>.</summary>
    public bool IsQuiet(DateTimeOffset written, DateTimeOffset now) => now - written > TimeSpan.FromDays(OutputDays);

    /// <summary>Whether <paramref name="freeGigabytes"/> of room is too little to build without a sweep first.</summary>
    public bool IsLow(double freeGigabytes) => freeGigabytes < LowDiskGigabytes;
}
