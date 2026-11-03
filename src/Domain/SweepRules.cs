namespace Snail.MCP.BuildBridge.Domain;

/// <summary>How old something on a host must be before a sweep removes it, and how little room makes a sweep run first.</summary>
/// <remarks>
/// Nothing on a host is the only copy of anything — a workspace is a copy of this machine's, outputs and caches rebuild
/// themselves, a job's log was already answered — so a sweep erases rather than keeping a bin. The ages are the owner's
/// decision of 2026-11-03: a branch not built for two weeks is done with; a job's log is read within a month or never.
/// </remarks>
/// <param name="WorkspaceDays">Days a workspace may go without a build before it is removed.</param>
/// <param name="JobDays">Days a job's log and artifacts nobody fetched are kept.</param>
/// <param name="ImageDays">Days an image a build made is kept.</param>
/// <param name="BuildCacheHours">Hours of Docker's build cache that are kept when the cache is pruned.</param>
/// <param name="LowDiskGigabytes">Free gigabytes below which a sweep runs before a build does.</param>
public sealed record SweepRules(int WorkspaceDays = 14, int JobDays = 30, int ImageDays = 7, int BuildCacheHours = 24, double LowDiskGigabytes = 20)
{
    /// <summary>Whether a workspace built or delivered last at <paramref name="lastUsed"/> is done with at <paramref name="now"/>.</summary>
    public bool IsStale(DateTimeOffset lastUsed, DateTimeOffset now) => now - lastUsed > TimeSpan.FromDays(WorkspaceDays);

    /// <summary>Whether <paramref name="freeGigabytes"/> of room is too little to build without a sweep first.</summary>
    public bool IsLow(double freeGigabytes) => freeGigabytes < LowDiskGigabytes;
}
