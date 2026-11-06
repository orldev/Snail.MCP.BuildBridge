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
        Assert.Equal("6h", items[3].Name);
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
    public void AfterRun_OneRepository_TakesItsStaleWorkspacesOrphanDerivedAndTheBuildCache()
    {
        var items = Sweep.AfterRun(Of(Fresh, Stale, Other), Rules, Now, "Anonym");

        Assert.Equal(["Anonym/old", "Gone-feat", "6h"], items.Select(item => item.Name));
    }

    [Fact]
    public void Plan_PastTenMinutes_IsNoLongerCurrent()
    {
        var plan = new SweepPlan("p1", Mini, SweepScope.Caches, [], Now);

        Assert.True(plan.IsCurrent(Now.AddMinutes(9)));
        Assert.False(plan.IsCurrent(Now.AddMinutes(11)));
    }

    /// <summary>
    /// A branch built every day is never stale: what its builds left in its copy goes by its own age, and the build cache
    /// is pruned after every run rather than once the host runs short.
    /// </summary>
    [Fact]
    public void AfterRun_OfABranchBuiltEveryDay_TakesItsQuietOutputsAndTheBuildCache()
    {
        var quiet = new OutputState(Fresh.Key, "artifacts/app-dd", Now.AddDays(-8), 4_000);
        var written = new OutputState(Fresh.Key, "artifacts/mac-dd", Now.AddDays(-1), 3_000);
        var elsewhere = new OutputState(Other.Key, "artifacts/old-dd", Now.AddDays(-30), 2_000);
        var survey = Of(Fresh, Other) with { Outputs = [quiet, written, elsewhere] };

        var items = Sweep.AfterRun(survey, Rules, Now, "Anonym");

        var output = Assert.Single(items, item => item.Kind == SweepKind.Output);
        Assert.Equal("Anonym/main/artifacts/app-dd", output.Name);
        Assert.Equal(4_000, output.Bytes);
        Assert.Equal("6h", Assert.Single(items, item => item.Kind == SweepKind.BuildCache).Name);
    }

    /// <summary>The outputs of a workspace that goes whole are not named again.</summary>
    [Fact]
    public void Decide_Caches_OutputsOfAStaleWorkspace_GoWithIt()
    {
        var inStale = new OutputState(Stale.Key, "artifacts/app-dd", Now.AddDays(-30), 4_000);
        var inFresh = new OutputState(Fresh.Key, "artifacts/tests/a.xcresult", Now.AddDays(-9), 1_000);
        var survey = Of(Fresh, Stale) with { Outputs = [inStale, inFresh] };

        var items = Sweep.Decide(survey, Rules, Now, SweepScope.Caches);

        Assert.Equal(["Anonym/main/artifacts/tests/a.xcresult"], items.Where(item => item.Kind == SweepKind.Output).Select(item => item.Name));
        Assert.Contains(items, item => item.Kind == SweepKind.Workspace && item.Name == "Anonym/old");
    }

    [Theory]
    [InlineData("artifacts/tests/run 1.xcresult", true)]
    [InlineData("artifacts/app-dd", true)]
    [InlineData("../other", false)]
    [InlineData("/etc", false)]
    [InlineData("artifacts/$(x)", false)]
    [InlineData("artifacts/a\"b", false)]
    [InlineData("", false)]
    public void OutputPath_IsPlain_OnlyInsideTheCopyAndUnreadByAShell(string path, bool isPlain) =>
        Assert.Equal(isPlain, OutputPath.IsPlain(path));
}
