using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tests.Support;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

/// <summary>This machine as a builder of its own: the root a test folder, every step the same as over ssh but for the transport.</summary>
public sealed class SshBuilderTests
{
    private static readonly Programs Programs = new();

    private static readonly GitCopies Copies = new(Programs);

    private static SshBuilder Builder()
    {
        var options = Options.Create(new BuildOptions { RunSeconds = 10, TransferSeconds = 30 });

        var shell = new RemoteShell(options);

        return new SshBuilder(Programs, new RsyncCourier(Programs, Copies, shell, options, TimeProvider.System), shell, options, TimeProvider.System);
    }

    private static Host LocalHost() => new("here", "localhost", TestFolder.Create());

    private static async Task<LocalCopy> CopyAsync(string repository) => (await Copies.ReadAsync(repository, CancellationToken.None)).Value!;

    [Fact]
    public async Task ProbeAsync_ThisMachine_AnswersWithItsToolchainsAndRoom()
    {
        var state = await Builder().ProbeAsync(LocalHost(), CancellationToken.None);

        Assert.True(state.IsReachable, state.Note);
        Assert.True(state.Has(Capability.Dotnet));
        Assert.True(state.FreeGigabytes > 0);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), state.Home);
    }

    [Fact]
    public async Task DeliverAsync_DryRun_ListsTheFilesAndSendsNothing()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());

        var answer = await Builder().DeliverAsync(host, copy, true, CancellationToken.None);

        var delivery = Assert.IsType<Delivery>(answer.Value);
        Assert.True(delivery.IsDryRun);
        Assert.Contains("README.md", delivery.Files);
        Assert.DoesNotContain(delivery.Files, file => file.Contains("bin", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(host.Root, copy.Workspace.CopyFolder, "README.md")));
    }

    [Fact]
    public async Task DeliverAsync_ACopy_MirrorsItWithoutWhatGitIgnoresAndLeavesAMarker()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create("Sample"));

        var answer = await Builder().DeliverAsync(host, copy, false, CancellationToken.None);

        Assert.Null(answer.Error);
        string workspace = Path.Combine(host.Root, "workspaces", "Sample", "main");
        string inside = Path.Combine(workspace, "Sample");
        Assert.True(File.Exists(Path.Combine(inside, "README.md")));
        Assert.True(File.Exists(Path.Combine(inside, "notes.txt")));
        Assert.True(Directory.Exists(Path.Combine(inside, ".git")));
        Assert.False(Directory.Exists(Path.Combine(inside, "secrets")));
        Assert.False(Directory.Exists(Path.Combine(inside, "src", "bin")));
        Assert.Contains("\"Repository\": \"Sample\"", await File.ReadAllTextAsync(Path.Combine(workspace, WorkspaceMarker.FileName)), StringComparison.Ordinal);
    }

    /// <summary>A build's outputs stay on the host between deliveries, so a build there stays incremental; a file deleted here is deleted there.</summary>
    [Fact]
    public async Task DeliverAsync_Again_KeepsTheHostsOutputsAndDeletesWhatWentAway()
    {
        var host = LocalHost();
        string repository = Repositories.Create();
        var copy = await CopyAsync(repository);
        var builder = Builder();
        await builder.DeliverAsync(host, copy, false, CancellationToken.None);
        string workspace = Path.Combine(host.Root, copy.Workspace.CopyFolder);
        Directory.CreateDirectory(Path.Combine(workspace, "src", "bin"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "src", "bin", "built.dll"), "kept");
        File.Delete(Path.Combine(repository, "notes.txt"));

        var answer = await builder.DeliverAsync(host, copy, false, CancellationToken.None);

        Assert.Contains("notes.txt", answer.Value!.Files);
        Assert.True(File.Exists(Path.Combine(workspace, "src", "bin", "built.dll")));
        Assert.False(File.Exists(Path.Combine(workspace, "notes.txt")));
    }

    [Fact]
    public async Task DeliverAsync_LinkedWorktree_LeavesTheDeadGitFileBehind()
    {
        var host = LocalHost();
        string repository = Repositories.Create();
        string worktree = Path.Combine(TestFolder.Create(), "side");
        Repositories.Git(repository, "worktree", "add", "-q", "-b", "side", worktree);
        var copy = await CopyAsync(worktree);

        await Builder().DeliverAsync(host, copy, false, CancellationToken.None);

        string workspace = Path.Combine(host.Root, copy.Workspace.CopyFolder);
        Assert.True(File.Exists(Path.Combine(workspace, "README.md")));
        Assert.False(File.Exists(Path.Combine(workspace, ".git")));
    }

    [Fact]
    public async Task ExecuteAsync_ACommand_RunsInTheWorkspaceWithItsOutputInTheLog()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var builder = Builder();
        await builder.DeliverAsync(host, copy, false, CancellationToken.None);
        string log = Path.Combine(TestFolder.Create(), "run.log");

        var answer = await builder.ExecuteAsync(host, copy.Workspace, ["sh", "-c", "pwd; cat README.md; echo oops >&2; exit 4"], log, TimeSpan.FromSeconds(10), CancellationToken.None);

        var execution = Assert.IsType<Execution>(answer.Value);
        Assert.Equal(4, execution.ExitCode);
        string written = await File.ReadAllTextAsync(log);
        Assert.Contains(copy.Workspace.CopyFolder, written, StringComparison.Ordinal);
        Assert.Contains("# Sample", written, StringComparison.Ordinal);
        Assert.Contains("oops", written, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(host.Root, copy.Workspace.Folder, WorkspaceMarker.BuiltFileName)));
        Assert.False(File.Exists(Path.Combine(host.Root, copy.Workspace.Folder, ".s3l-build.pid")));
    }

    [Fact]
    public async Task ExecuteAsync_ACommandPastItsTime_IsStoppedOnTheHostToo()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var builder = Builder();
        await builder.DeliverAsync(host, copy, false, CancellationToken.None);
        string log = Path.Combine(TestFolder.Create(), "run.log");
        string witness = Path.Combine(TestFolder.Create(), "still-running");

        var answer = await builder.ExecuteAsync(host, copy.Workspace, ["sh", "-c", $"sleep 30; touch '{witness}'"], log, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(answer.Value!.IsTimedOut);
        Assert.False(File.Exists(Path.Combine(host.Root, copy.Workspace.Folder, ".s3l-build.pid")));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(File.Exists(witness));
    }

    [Fact]
    public async Task CollectAsync_PathsOfTheWorkspace_LandUnderTheFolderByTheirPath()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var builder = Builder();
        await builder.DeliverAsync(host, copy, false, CancellationToken.None);
        string folder = TestFolder.Create();

        var answer = await builder.CollectAsync(host, copy.Workspace, ["src/Program.cs", "README.md"], folder, CancellationToken.None);

        var collected = Assert.IsType<Collected>(answer.Value);
        Assert.Equal(["README.md", "src/Program.cs"], collected.Files);
        Assert.True(File.Exists(Path.Combine(folder, "src", "Program.cs")));
    }

    [Fact]
    public async Task CollectAsync_PathOutsideTheWorkspace_IsRefused()
    {
        var answer = await Builder().CollectAsync(LocalHost(), new WorkspaceKey("Sample", "main"), ["../other"], TestFolder.Create(), CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Contains("not a path inside the workspace", answer.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveAsync_AWorkspaceOfThisServers_IsRemovedOnceAndKeptWhenNotItsOwn()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var builder = Builder();
        await builder.DeliverAsync(host, copy, false, CancellationToken.None);
        string stranger = Path.Combine(host.Root, "workspaces", "Other", "main");
        Directory.CreateDirectory(stranger);

        var removed = await builder.RemoveAsync(host, copy.Workspace, CancellationToken.None);
        var kept = await builder.RemoveAsync(host, new WorkspaceKey("Other", "main"), CancellationToken.None);

        Assert.True(removed.Value);
        Assert.False(Directory.Exists(Path.Combine(host.Root, copy.Workspace.Folder)));
        Assert.False(kept.Value);
        Assert.True(Directory.Exists(stranger));
    }

    [Fact]
    public async Task MirrorAsync_AFolder_LandsUnderTheRootAndKeepsUpWithChanges()
    {
        var host = LocalHost();
        string feed = TestFolder.Create();
        await File.WriteAllTextAsync(Path.Combine(feed, "a.nupkg"), "a");
        var builder = Builder();

        var first = await builder.MirrorAsync(host, feed, "feeds/LocalPackages", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(feed, "b.nupkg"), "b");
        var second = await builder.MirrorAsync(host, feed, "feeds/LocalPackages", CancellationToken.None);

        Assert.Null(first.Error);
        Assert.Equal(["b.nupkg"], second.Value!.Files);
        Assert.True(File.Exists(Path.Combine(host.Root, "feeds", "LocalPackages", "a.nupkg")));
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_IsStoppedOnTheHostToo()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var builder = Builder();
        await builder.DeliverAsync(host, copy, false, CancellationToken.None);
        string log = Path.Combine(TestFolder.Create(), "run.log");
        string witness = Path.Combine(TestFolder.Create(), "still-running");
        using var cancelling = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.ExecuteAsync(host, copy.Workspace, ["sh", "-c", $"sleep 30; touch '{witness}'"], log, TimeSpan.FromSeconds(60), cancelling.Token));

        Assert.False(File.Exists(Path.Combine(host.Root, copy.Workspace.Folder, ".s3l-build.pid")));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(File.Exists(witness));
    }

    [Fact]
    public async Task SurveyAsync_WorkspacesWithMarkers_AreFoundWithTheirAgeAndSize()
    {
        var host = LocalHost();
        var builder = Builder();
        var fresh = await CopyAsync(Repositories.Create("Fresh"));
        var old = await CopyAsync(Repositories.Create("Old"));
        await builder.DeliverAsync(host, fresh, false, CancellationToken.None);
        await builder.DeliverAsync(host, old, false, CancellationToken.None);
        File.SetLastWriteTimeUtc(Path.Combine(host.Root, old.Workspace.Folder, WorkspaceMarker.FileName), DateTime.UtcNow.AddDays(-20));
        Directory.CreateDirectory(Path.Combine(host.Root, "derived", "Gone-main"));

        var answer = await builder.SurveyAsync(host, CancellationToken.None);

        var survey = Assert.IsType<Survey>(answer.Value);
        Assert.Equal(["Fresh/main", "Old/main"], survey.Workspaces.Select(state => $"{state.Key.Repository}/{state.Key.Branch}").Order(StringComparer.Ordinal));
        Assert.All(survey.Workspaces, state => Assert.True(state.Bytes > 0));
        Assert.True(survey.Workspaces.Single(state => state.Key.Repository == "Old").LastUsed < DateTimeOffset.UtcNow.AddDays(-19));
        Assert.Equal(["Gone-main"], survey.Derived);
        Assert.True(survey.FreeGigabytes > 0);
    }

    [Fact]
    public async Task CarryOutAsync_AWorkspaceAndADerivedFolder_AreRemoved()
    {
        var host = LocalHost();
        var builder = Builder();
        var copy = await CopyAsync(Repositories.Create("Sample"));
        await builder.DeliverAsync(host, copy, false, CancellationToken.None);
        string derived = Path.Combine(host.Root, "derived", "Sample-main");
        Directory.CreateDirectory(derived);

        var workspace = await builder.CarryOutAsync(host, new SweepItem(SweepKind.Workspace, "Sample/main", null, "test"), CancellationToken.None);
        var folder = await builder.CarryOutAsync(host, new SweepItem(SweepKind.Derived, "Sample-main", null, "test"), CancellationToken.None);

        Assert.True(workspace.Value);
        Assert.True(folder.Value);
        Assert.False(Directory.Exists(Path.Combine(host.Root, copy.Workspace.Folder)));
        Assert.False(Directory.Exists(derived));
    }

    [Fact]
    public void SurveyOf_DockerLines_AreReadWithTheirMomentsAndSizes()
    {
        const string output = """
            s3l-ws Anonym main 1762160000 1762170000 20480
            s3l-derived Anonym-main
            s3l-image 561bb90dbe42 2026-10-30 19:52:13 +0300 MSK 228MB
            s3l-buildcache 5.931GB
            s3l-packages 1048576
            s3l-free 612
            """;

        var survey = Surveys.Of(new Host("mini", "mini"), output);

        var workspace = Assert.Single(survey.Workspaces);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762170000), workspace.LastUsed);
        Assert.Equal(20480L * 1024, workspace.Bytes);
        var image = Assert.Single(survey.Images);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 16, 52, 13, TimeSpan.Zero), image.CreatedAt);
        Assert.Equal("228MB", image.Size);
        Assert.Equal(5.931, survey.BuildCacheGigabytes, 3);
        Assert.Equal(1.0, survey.PackagesGigabytes, 3);
        Assert.Equal(612, survey.FreeGigabytes);
    }

    [Fact]
    public void SurveyOf_LinesOfVersions_AreTheSdksTheWishesTheToolsAndTheImages()
    {
        const string output = """
            s3l-sdk 10.0.302
            s3l-sdk 10.0.100
            s3l-sdkwanted 10.0.302
            s3l-sdkwanted
            s3l-buildtools 36.0.0
            s3l-ndk 28.2.13676358
            s3l-toolimage s3l-build-dotnet:10.0.302
            s3l-free 612
            """;

        var survey = Surveys.Of(new Host("mini", "mini"), output);

        Assert.Equal(["10.0.302", "10.0.100"], survey.Sdks);
        Assert.Equal(["10.0.302"], survey.SdksWanted);
        Assert.Equal(["36.0.0"], survey.BuildTools);
        Assert.Equal(["28.2.13676358"], survey.Ndks);
        Assert.Equal(["s3l-build-dotnet:10.0.302"], survey.ToolImages);
    }

    [Fact]
    public async Task CarryOutAsync_AnSdkUnderTheRoot_IsRemovedByVersion()
    {
        var host = LocalHost();
        string sdk = Path.Combine(host.Root, "dotnet", "sdk", "9.0.100");
        Directory.CreateDirectory(sdk);

        var removed = await Builder().CarryOutAsync(host, new SweepItem(SweepKind.Sdk, "9.0.100", null, "test"), CancellationToken.None);
        var refused = await Builder().CarryOutAsync(host, new SweepItem(SweepKind.Sdk, "../../workspaces", null, "test"), CancellationToken.None);

        Assert.True(removed.Value);
        Assert.False(Directory.Exists(sdk));
        Assert.False(refused.Value);
    }

    [SimulatorFact]
    public async Task AcquireSimulatorAsync_ADeviceOfThisMac_IsClonedAndGivenBack()
    {
        var host = LocalHost();
        var builder = Builder();
        var programs = new Programs();
        var devices = await programs.RunAsync("xcrun", ["simctl", "list", "devices", "available"], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        string device = devices.Output.Split('\n').Select(line => line.Trim()).First(line => line.StartsWith("iPhone", StringComparison.Ordinal) && line.Contains("(Shutdown)", StringComparison.Ordinal)).Split(" (")[0];
        string clone = $"{XcodeCommand.SimulatorPrefix}test";

        var acquired = await builder.AcquireSimulatorAsync(host, device, clone, CancellationToken.None);
        var listed = await programs.RunAsync("xcrun", ["simctl", "list", "devices"], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        await builder.ReleaseSimulatorAsync(host, acquired.Value!, CancellationToken.None);
        var after = await programs.RunAsync("xcrun", ["simctl", "list", "devices"], null, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Null(acquired.Error);
        Assert.Contains($"{clone} ({acquired.Value})", listed.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(clone, after.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcquireSimulatorAsync_ADeviceThatIsNotThere_SaysSo()
    {
        var answer = await Builder().AcquireSimulatorAsync(LocalHost(), "no such phone", $"{XcodeCommand.SimulatorPrefix}none", CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Contains("No simulator was cloned", answer.Error, StringComparison.Ordinal);
    }

    [HostFact]
    public async Task OverSsh_ARealHost_TakesADeliveryARunAndARemoval()
    {
        var host = new Host("real", HostFactAttribute.Address);
        var copy = await CopyAsync(Repositories.Create("S3lBuildBridgeProbe"));
        var builder = Builder();
        string log = Path.Combine(TestFolder.Create(), "run.log");

        var state = await builder.ProbeAsync(host, CancellationToken.None);
        var delivered = await builder.DeliverAsync(host, copy, false, CancellationToken.None);
        var ran = await builder.ExecuteAsync(host, copy.Workspace, ["cat", "README.md"], log, TimeSpan.FromSeconds(30), CancellationToken.None);
        var removed = await builder.RemoveAsync(host, copy.Workspace, CancellationToken.None);

        Assert.True(state.IsReachable, state.Note);
        Assert.Null(delivered.Error);
        Assert.Equal(0, ran.Value!.ExitCode);
        Assert.Contains("# Sample", await File.ReadAllTextAsync(log), StringComparison.Ordinal);
        Assert.True(removed.Value);
    }
}
