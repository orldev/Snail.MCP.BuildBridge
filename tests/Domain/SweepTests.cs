namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class SweepTests
{
    private static readonly Host Mini = new("mini", "mini");

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly SweepRules Rules = new();

    private static readonly WorkspaceState Fresh = new(new WorkspaceKey("Anonym", "main"), Now.AddDays(-1), 100);

    private static readonly WorkspaceState Stale = new(new WorkspaceKey("Anonym", "old"), Now.AddDays(-20), 200);

    private static readonly WorkspaceState Other = new(new WorkspaceKey("Keynex", "main"), Now.AddDays(-20), 300);

    private static Survey Of(params WorkspaceState[] workspaces) => new(Mini, workspaces, ["Anonym-main", "Gone-feat"], [new ImageState("a1", Now.AddDays(-10), "1GB"), new ImageState("b2", Now.AddDays(-1), "1GB")], 5.9, 2.0, 100);

    [Fact]
    public void Decide_Workspace_IsThatWorkspaceAlone()
    {
        var items = Sweep.Decide(Of(Fresh, Stale), Rules, Now, SweepScope.Workspace, Fresh.Key);

        var item = Assert.Single(items);
        Assert.Equal("Anonym/main", item.Name);
        Assert.Equal(100, item.Bytes);
    }

    [Fact]
    public void Decide_Repository_IsEveryBranchOfItWhateverTheirAge()
    {
        var items = Sweep.Decide(Of(Fresh, Stale, Other), Rules, Now, SweepScope.Repository, repository: "anonym");

        Assert.Equal(["Anonym/main", "Anonym/old"], items.Select(item => item.Name));
    }

    [Fact]
    public void Decide_Caches_IsWhatWentStaleAndWhatIsOrphaned()
    {
        var items = Sweep.Decide(Of(Fresh, Stale), Rules, Now, SweepScope.Caches);

        Assert.Equal([SweepKind.Workspace, SweepKind.Derived, SweepKind.Images, SweepKind.BuildCache], items.Select(item => item.Kind));
        Assert.Equal("Anonym/old", items[0].Name);
        Assert.Equal("Gone-feat", items[1].Name);
        Assert.Equal("168h", items[2].Name);
        Assert.Contains("1 images", items[2].Reason, StringComparison.Ordinal);
        Assert.Equal("24h", items[3].Name);
    }

    /// <summary>What an update left behind goes in caches: an SDK nothing names, build tools and NDKs older than the newest unless the settings name them, toolchain images of another SDK.</summary>
    [Fact]
    public void Outdated_VersionsAnUpdateLeftBehind_AreNamedUnlessSettingsOrAWorkspaceKeepThem()
    {
        var survey = Of(Fresh) with
        {
            Sdks = ["10.0.100", "10.0.302", "9.0.305"],
            SdksWanted = ["9.0.305"],
            BuildTools = ["36.0.0", "36.1.0", "35.0.0"],
            Ndks = ["28.1.13356709", "28.2.13676358"],
            ToolImages = ["s3l-build-dotnet:10.0.302", "s3l-build-dotnet:10.0.100"]
        };
        var rules = new SweepRules { KeptSdk = "10.0.302", KeptPackages = ["build-tools;36.0.0"], KeptImages = ["s3l-build-dotnet:10.0.302"] };

        var items = Sweep.Outdated(survey, rules);

        Assert.Equal(["10.0.100", "build-tools;35.0.0", "ndk;28.1.13356709", "s3l-build-dotnet:10.0.100"], items.Select(item => item.Name));
        Assert.Equal([SweepKind.Sdk, SweepKind.Package, SweepKind.Package, SweepKind.Images], items.Select(item => item.Kind));
        Assert.Contains(Sweep.Decide(survey, rules, Now, SweepScope.Caches), item => item.Name == "10.0.100");
    }

    [Fact]
    public void Decide_Everything_TakesAllButTheSdk()
    {
        var items = Sweep.Decide(Of(Fresh, Stale), Rules, Now, SweepScope.Everything);

        Assert.Equal(["Anonym/main", "Anonym/old", "Anonym-main", "Gone-feat", "all", "all", "nuget"], items.Select(item => item.Name));
        Assert.Equal(SweepKind.Packages, items[^1].Kind);
    }

    [Fact]
    public void AfterRun_OneRepository_TakesItsStaleWorkspacesAndOrphanDerivedOnly()
    {
        var items = Sweep.AfterRun(Of(Fresh, Stale, Other), Rules, Now, "Anonym");

        Assert.Equal(["Anonym/old", "Gone-feat"], items.Select(item => item.Name));
    }

    [Fact]
    public void Plan_PastTenMinutes_IsNoLongerCurrent()
    {
        var plan = new SweepPlan("p1", Mini, SweepScope.Caches, [], Now);

        Assert.True(plan.IsCurrent(Now.AddMinutes(9)));
        Assert.False(plan.IsCurrent(Now.AddMinutes(11)));
    }
}
