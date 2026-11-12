namespace Snail.MCP.BuildBridge.Domain;

/// <summary>How much of a host a sweep looks at.</summary>
public enum SweepScope
{
    /// <summary>One workspace: a repository on one branch.</summary>
    Workspace,

    /// <summary>Every workspace of one repository.</summary>
    Repository,

    /// <summary>What rebuilds itself and has gone stale: workspaces past their age, derived data of workspaces that are gone, old images and build cache, old logs.</summary>
    Caches,

    /// <summary>Every workspace, derived data, image, the build cache and the packages; the SDK stays.</summary>
    Everything
}

/// <summary>A workspace as a survey found it: when it was last delivered or built, and its size.</summary>
public sealed record WorkspaceState(WorkspaceKey Key, DateTimeOffset LastUsed, long Bytes);

/// <summary>What a build left inside a workspace's copy that the next build makes again: Xcode's derived data wherever <c>-derivedDataPath</c> put it, or a test bundle.</summary>
/// <param name="Key">The workspace, as its folders are named.</param>
/// <param name="Path">The folder, relative to the copy.</param>
/// <param name="LastWritten">The newest write two levels down, where every build leaves a log or a product.</param>
/// <param name="Bytes">Its size.</param>
public sealed record OutputState(WorkspaceKey Key, string Path, DateTimeOffset LastWritten, long Bytes);

/// <summary>An image a build made, as docker lists it.</summary>
public sealed record ImageState(string Id, DateTimeOffset CreatedAt, string Size);

/// <summary>A host's root as a survey found it.</summary>
/// <param name="Host">The host.</param>
/// <param name="Workspaces">Every workspace with this server's marker.</param>
/// <param name="Derived">The names of the derived data folders, <c>&lt;repository&gt;-&lt;branch&gt;</c>.</param>
/// <param name="Images">Every image carrying the owner label.</param>
/// <param name="BuildCacheGigabytes">Docker's build cache.</param>
/// <param name="PackagesGigabytes">The NuGet packages, Gradle's own files, npm's cache and Playwright's browsers under the root.</param>
/// <param name="FreeGigabytes">The room left under the host's home.</param>
public sealed record Survey(Host Host, IReadOnlyList<WorkspaceState> Workspaces, IReadOnlyList<string> Derived, IReadOnlyList<ImageState> Images, double BuildCacheGigabytes, double PackagesGigabytes, double? FreeGigabytes)
{
    /// <summary>The .NET SDKs under the root, by version.</summary>
    public IReadOnlyList<string> Sdks { get; init; } = [];

    /// <summary>The SDK versions the workspaces' global.json files name, which a sweep keeps.</summary>
    public IReadOnlyList<string> SdksWanted { get; init; } = [];

    /// <summary>The Android build tools under the root, by version.</summary>
    public IReadOnlyList<string> BuildTools { get; init; } = [];

    /// <summary>The NDKs under the root, by version.</summary>
    public IReadOnlyList<string> Ndks { get; init; } = [];

    /// <summary>The toolchain images of this server on the host, by tag.</summary>
    public IReadOnlyList<string> ToolImages { get; init; } = [];

    /// <summary>Xcode's derived data and test bundles inside the workspaces' copies.</summary>
    public IReadOnlyList<OutputState> Outputs { get; init; } = [];
}

/// <summary>What one item of a sweep is.</summary>
public enum SweepKind
{
    /// <summary>A workspace, named <c>repository/branch</c>.</summary>
    Workspace,

    /// <summary>A derived data folder, named as it is under derived.</summary>
    Derived,

    /// <summary>The images carrying the owner label older than the age named, such as <c>168h</c>; <c>all</c> for every one.</summary>
    Images,

    /// <summary>Docker's build cache not used within the age named, such as <c>6h</c>; <c>all</c> for every entry.</summary>
    BuildCache,

    /// <summary>The NuGet packages, Gradle's own files, npm's cache and Playwright's browsers under the root.</summary>
    Packages,

    /// <summary>A session of this machine to a host, by name: ended when its copy here is gone, paused when its workspace has gone unused for a while.</summary>
    Session,

    /// <summary>A .NET SDK under the root, by version, that no settings and no workspace name any more.</summary>
    Sdk,

    /// <summary>An Android SDK package, as sdkmanager names it, older than the newest of its family.</summary>
    Package,

    /// <summary>The logs on this machine older than the age named.</summary>
    Logs,

    /// <summary>Xcode's derived data or a test bundle inside a workspace's copy, named <c>repository/branch/path</c>.</summary>
    Output
}

/// <summary>One thing a sweep removes, and why.</summary>
public sealed record SweepItem(SweepKind Kind, string Name, long? Bytes, string Reason);

/// <summary>A sweep decided and shown, carried out only as it stands.</summary>
public sealed record SweepPlan(string Id, Host Host, SweepScope Scope, IReadOnlyList<SweepItem> Items, DateTimeOffset MadeAt)
{
    /// <summary>How long a plan may be carried out after it was shown.</summary>
    public static TimeSpan Keeps { get; } = TimeSpan.FromMinutes(10);

    /// <summary>The bytes the plan knows it gives back; images and the build cache are not counted.</summary>
    public long KnownBytes => Items.Sum(item => item.Bytes ?? 0);

    /// <summary>Whether the plan may still be carried out at <paramref name="now"/>.</summary>
    public bool IsCurrent(DateTimeOffset now) => now - MadeAt <= Keeps;
}

/// <summary>One item of a sweep as it went.</summary>
public sealed record SweepResult(SweepItem Item, bool IsDone, string? Note);

/// <summary>What a sweep decides from a survey: pure rules, no host touched.</summary>
public static class Sweep
{
    /// <summary>The items a sweep of <paramref name="scope"/> removes from what <paramref name="survey"/> found.</summary>
    /// <param name="survey">The host as it stands.</param>
    /// <param name="rules">The ages.</param>
    /// <param name="now">The moment the ages are counted from.</param>
    /// <param name="scope">How much is looked at.</param>
    /// <param name="workspace">The workspace a Workspace sweep means.</param>
    /// <param name="repository">The repository a Repository sweep means.</param>
    public static IReadOnlyList<SweepItem> Decide(Survey survey, SweepRules rules, DateTimeOffset now, SweepScope scope, WorkspaceKey? workspace = null, string? repository = null) => scope switch
    {
        SweepScope.Workspace => [.. survey.Workspaces.Where(state => state.Key == workspace).Select(state => Of(state, "asked for"))],
        SweepScope.Repository => [.. survey.Workspaces.Where(state => string.Equals(state.Key.Repository, repository, StringComparison.OrdinalIgnoreCase)).Select(state => Of(state, "asked for"))],
        SweepScope.Caches => Stale(survey, rules, now),
        SweepScope.Everything => All(survey),
        _ => []
    };

    /// <summary>The items a sweep after a run of <paramref name="repository"/> removes: that repository's stale workspaces, the quiet outputs inside its copies, the derived data of workspaces that are gone, and the build cache past its hours.</summary>
    /// <remarks>
    /// A branch built every day is never stale, and what its builds leave inside its copy — where a delivery never looks,
    /// since it leaves out what git ignores — grew on with it. The build cache was pruned only once the host ran short of
    /// room, which on a large disk is hundreds of gigabytes later.
    /// </remarks>
    public static IReadOnlyList<SweepItem> AfterRun(Survey survey, SweepRules rules, DateTimeOffset now, string repository)
    {
        var stale = survey.Workspaces.Where(state => IsOf(state.Key, repository) && rules.IsStale(state.LastUsed, now)).ToList();
        var kept = survey.Outputs.Where(output => IsOf(output.Key, repository) && !stale.Exists(state => state.Key == output.Key));
        var items = new List<SweepItem>(stale.Select(state => Of(state, $"not built for {rules.WorkspaceDays} days")));

        items.AddRange(Quiet(kept, rules, now));
        items.AddRange(OrphanDerived(survey));

        if (survey.BuildCacheGigabytes > 0)
            items.Add(CacheOf(survey, rules));

        return items;
    }

    /// <summary>The workspaces left alone long enough for the sessions that mirror into them to rest, without those the sweep after a run of <paramref name="repository"/> removes.</summary>
    /// <remarks>
    /// A session holds a connection to its host for as long as it lives, and every session reconnects at once when the
    /// host's sshd restarts: on 2026-11-12 fifty-six of them did, more than the forty-two handshakes launchd lets a Mac's
    /// sshd take at a time, and the host turned connections away for minutes. A paused session holds none.
    /// </remarks>
    public static IReadOnlyList<WorkspaceKey> Idle(Survey survey, SweepRules rules, DateTimeOffset now, string repository)
    {
        var idle = survey.Workspaces.Where(state => rules.IsIdle(state.LastUsed, now));
        var kept = idle.Where(state => !(IsOf(state.Key, repository) && rules.IsStale(state.LastUsed, now)));

        return [.. kept.Select(state => state.Key)];
    }

    private static IReadOnlyList<SweepItem> Stale(Survey survey, SweepRules rules, DateTimeOffset now)
    {
        var stale = survey.Workspaces.Where(state => rules.IsStale(state.LastUsed, now)).ToList();
        var kept = survey.Outputs.Where(output => !stale.Exists(state => state.Key == output.Key));
        var images = survey.Images.Where(image => now - image.CreatedAt > TimeSpan.FromDays(rules.ImageDays)).ToList();
        var items = new List<SweepItem>(stale.Select(state => Of(state, $"not built for {rules.WorkspaceDays} days")));

        items.AddRange(Quiet(kept, rules, now));
        items.AddRange(OrphanDerived(survey));
        if (images.Count > 0)
            items.Add(new SweepItem(SweepKind.Images, $"{rules.ImageDays * 24}h", null, $"{images.Count} images older than {rules.ImageDays} days"));
        if (survey.BuildCacheGigabytes > 0)
            items.Add(CacheOf(survey, rules));
        items.AddRange(Outdated(survey, rules));

        return items;
    }

    private static IEnumerable<SweepItem> Quiet(IEnumerable<OutputState> outputs, SweepRules rules, DateTimeOffset now) =>
        outputs
            .Where(output => rules.IsQuiet(output.Path, output.LastWritten, now))
            .Select(output => new SweepItem(SweepKind.Output, $"{output.Key.Repository}/{output.Key.Branch}/{output.Path}", Measured(output.Bytes), $"nothing was written into it for {rules.DaysOf(output.Path)} days"));

    private static SweepItem CacheOf(Survey survey, SweepRules rules) =>
        new(SweepKind.BuildCache, $"{rules.BuildCacheHours}h", null, $"build cache not used for {rules.BuildCacheHours} hours, of {survey.BuildCacheGigabytes:0.0} GB");

    private static bool IsOf(WorkspaceKey key, string repository) => string.Equals(key.Repository, repository, StringComparison.OrdinalIgnoreCase);

    /// <summary>The versions an update left behind: SDKs no settings and no workspace name, build tools and NDKs older than the newest unless the settings name them, and toolchain images of another SDK.</summary>
    public static IReadOnlyList<SweepItem> Outdated(Survey survey, SweepRules rules)
    {
        var wanted = survey.SdksWanted.Append(rules.KeptSdk).ToHashSet(StringComparer.Ordinal);
        var sdks = survey.Sdks.Where(version => !wanted.Contains(version)).Select(version => new SweepItem(SweepKind.Sdk, version, null, "no settings and no workspace name it"));
        var images = survey.ToolImages.Where(tag => !rules.KeptImages.Contains(tag, StringComparer.Ordinal)).Select(tag => new SweepItem(SweepKind.Images, tag, null, "a toolchain image the settings do not name"));

        return [.. sdks, .. Older("build-tools", survey.BuildTools, rules.KeptPackages), .. Older("ndk", survey.Ndks, rules.KeptPackages), .. images];
    }

    private static IEnumerable<SweepItem> Older(string family, IReadOnlyList<string> versions, IReadOnlyList<string> kept)
    {
        if (versions.Count < 2)
            return [];

        string newest = versions.MaxBy(version => SdkWish.Parsed(version) ?? new Version(0, 0))!;
        var older = versions.Where(version => version != newest && !kept.Contains($"{family};{version}", StringComparer.Ordinal));

        return older.Select(version => new SweepItem(SweepKind.Package, $"{family};{version}", null, $"older than {newest}"));
    }

    private static IReadOnlyList<SweepItem> All(Survey survey)
    {
        var items = survey.Workspaces.Select(state => Of(state, "everything")).ToList();

        items.AddRange(survey.Derived.Select(name => new SweepItem(SweepKind.Derived, name, null, "everything")));
        if (survey.Images.Count > 0)
            items.Add(new SweepItem(SweepKind.Images, "all", null, $"{survey.Images.Count} images of this server's"));
        if (survey.BuildCacheGigabytes > 0)
            items.Add(new SweepItem(SweepKind.BuildCache, "all", null, $"build cache of {survey.BuildCacheGigabytes:0.0} GB"));
        if (survey.PackagesGigabytes > 0)
            items.Add(new SweepItem(SweepKind.Packages, "nuget", (long)(survey.PackagesGigabytes * 1024 * 1024 * 1024), "the packages, Gradle's files, npm's cache and Playwright's browsers under the root; a restore brings them back"));

        return items;
    }

    private static IEnumerable<SweepItem> OrphanDerived(Survey survey)
    {
        var living = survey.Workspaces.Select(state => $"{WorkspaceKey.Safe(state.Key.Repository)}-{WorkspaceKey.Safe(state.Key.Branch)}").ToHashSet(StringComparer.Ordinal);

        return survey.Derived.Where(name => !living.Contains(name)).Select(name => new SweepItem(SweepKind.Derived, name, null, "its workspace is gone"));
    }

    private static SweepItem Of(WorkspaceState state, string reason) => new(SweepKind.Workspace, $"{state.Key.Repository}/{state.Key.Branch}", Measured(state.Bytes), reason);

    /// <summary>A size as an item carries it: unknown when the survey measured none.</summary>
    private static long? Measured(long bytes) => bytes > 0 ? bytes : null;
}
