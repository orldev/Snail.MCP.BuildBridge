using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tests.Support;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

/// <summary>A Mutagen session mirroring a copy into a workspace on this machine: made on the first delivery, caught up on the next, let go of with the workspace.</summary>
public sealed class MutagenCourierTests
{
    private static readonly Programs Programs = new();

    private static readonly GitCopies Copies = new(Programs);

    private static MutagenCourier Courier()
    {
        var options = Options.Create(new BuildOptions { Sync = Delivery.Mutagen, TransferSeconds = 60 });
        var shell = new RemoteShell(options);

        return new MutagenCourier(Programs, Copies, new RsyncCourier(Programs, Copies, shell, options, TimeProvider.System), shell, options, TimeProvider.System, TestFolder.Create());
    }

    private static Host LocalHost() => new($"here-{Path.GetRandomFileName()[..6]}", "localhost", TestFolder.Create());

    private static async Task<LocalCopy> CopyAsync(string repository) => (await Copies.ReadAsync(repository, CancellationToken.None)).Value!;

    [MutagenFact]
    public async Task DeliverAsync_ACopy_MirrorsItWithoutWhatGitIgnoresAndKeepsFollowing()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var courier = Courier();

        try
        {
            var first = await courier.DeliverAsync(host, copy, false, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(copy.Path, "later.txt"), "later", CancellationToken.None);
            var second = await courier.DeliverAsync(host, copy, false, CancellationToken.None);

            string workspace = Path.Combine(host.Root, copy.Workspace.CopyFolder);
            Assert.Equal(Delivery.Mutagen, Assert.IsType<Delivery>(first.Value).Means);
            Assert.Null(second.Error);
            Assert.True(File.Exists(Path.Combine(workspace, "README.md")));
            Assert.True(File.Exists(Path.Combine(workspace, "later.txt")));
            Assert.False(Directory.Exists(Path.Combine(workspace, "bin")));
            Assert.False(Directory.Exists(Path.Combine(workspace, "secrets")));
        }
        finally
        {
            await courier.ForgetAsync(host, copy.Workspace, CancellationToken.None);
        }
    }

    [MutagenFact]
    public async Task DeliverAsync_DryRun_ListsTheDifferenceThroughRsyncAndMakesNoSession()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());

        var answer = await Courier().DeliverAsync(host, copy, true, CancellationToken.None);

        var delivery = Assert.IsType<Delivery>(answer.Value);
        Assert.True(delivery.IsDryRun);
        Assert.Equal(Delivery.Rsync, delivery.Means);
        Assert.Contains("README.md", delivery.Files);
        Assert.False(Directory.Exists(Path.Combine(host.Root, copy.Workspace.CopyFolder, "README.md")));
    }

    [MutagenFact]
    public async Task ForgetAsync_AfterADelivery_LeavesNoSessionBehind()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var courier = Courier();
        await courier.DeliverAsync(host, copy, false, CancellationToken.None);

        await courier.ForgetAsync(host, copy.Workspace, CancellationToken.None);

        var listed = await Programs.RunAsync("mutagen", ["sync", "list", MutagenCourier.SessionOf(host, copy.Workspace)], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.False(listed.IsSuccess);
    }

    [Fact]
    public void SessionOf_RepositoryWithDotsAndASlashedBranch_IsLettersDigitsAndDashes()
    {
        string session = MutagenCourier.SessionOf(new Host("mini", "dev@mini"), new WorkspaceKey("Snail.MCP.BuildBridge", "feat/web-3.0"));

        Assert.Equal("s3l-snail-mcp-buildbridge-feat-web-3-0-mini", session);
    }

    [Fact]
    public void SessionOf_TwoNamesAlikeForSixtyFourLetters_AreToldApart()
    {
        string stem = new('a', 70);
        string one = MutagenCourier.SessionOf(new Host("mini", "dev@mini"), new WorkspaceKey(stem, "one"));
        string other = MutagenCourier.SessionOf(new Host("mini", "dev@mini"), new WorkspaceKey(stem, "two"));

        Assert.NotEqual(one, other);
        Assert.True(one.Length <= 64);
        Assert.Matches("^[a-z0-9-]+$", one);
    }

    [Fact]
    public void ShapeOf_AnotherIgnore_IsAnotherShape()
    {
        string one = MutagenCourier.ShapeOf("/Users/me/Sample", "mini:~/.s3l-build/workspaces/Sample/main", ["bin", "obj"]);
        string other = MutagenCourier.ShapeOf("/Users/me/Sample", "mini:~/.s3l-build/workspaces/Sample/main", ["bin", "obj", "/secrets/"]);

        Assert.NotEqual(one, other);
        Assert.Equal(16, one.Length);
    }

    [Fact]
    public void ShapeIn_MutagensListOfOneSession_IsItsLabel()
    {
        const string json = """[{"name":"s3l-sample-main-mini","labels":{"s3l.shape":"0123456789abcdef"},"status":"watching"}]""";

        Assert.Equal("0123456789abcdef", MutagenCourier.ShapeIn(json));
        Assert.Null(MutagenCourier.ShapeIn("[]"));
    }

    /// <summary>Of this server's sessions, those whose copy is gone are the stranded ones; a session of another name is never touched.</summary>
    [Fact]
    public void StrandedIn_MutagensList_IsTheSessionsOfThisServerWhoseCopyIsGone()
    {
        const string json = """
            [
              {"name":"s3l-sample-main-mini","alpha":{"protocol":"local","path":"/Users/me/Sample"},"labels":{"s3l.shape":"a"}},
              {"name":"s3l-sample-wt-feat-mini","alpha":{"protocol":"local","path":"/Users/me/Sample.wt"},"labels":{"s3l.shape":"b"}},
              {"name":"mine","alpha":{"protocol":"local","path":"/Users/me/Gone"},"labels":{"s3l.shape":"c"}}
            ]
            """;

        var stranded = MutagenCourier.StrandedIn(json, path => path == "/Users/me/Sample");

        Assert.Equal(["s3l-sample-wt-feat-mini"], stranded);
        Assert.Empty(MutagenCourier.StrandedIn(string.Empty, _ => false));
    }

    /// <summary>Only a session that is awake holds a connection, so only those are paused, and never one of another name.</summary>
    [Fact]
    public void AwakeIn_MutagensList_IsTheSessionsOfThisServerThatAreNotPaused()
    {
        const string json = """
            [
              {"name":"s3l-sample-main-mini","paused":false,"status":"watching"},
              {"name":"s3l-sample-old-mini","paused":true,"status":"disconnected"},
              {"name":"mine","paused":false,"status":"watching"}
            ]
            """;

        Assert.Equal(["s3l-sample-main-mini"], MutagenCourier.AwakeIn(json));
        Assert.Empty(MutagenCourier.AwakeIn(string.Empty));
    }

    /// <summary>A session put to rest holds no connection; the next delivery wakes it and the copy is mirrored as before, and a second rest finds nothing awake.</summary>
    [MutagenFact]
    public async Task RestAsync_AfterADelivery_PausesTheSessionAndTheNextDeliveryResumesIt()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var courier = Courier();

        try
        {
            await courier.DeliverAsync(host, copy, false, CancellationToken.None);
            var rested = await courier.RestAsync(host, [copy.Workspace], CancellationToken.None);
            var again = await courier.RestAsync(host, [copy.Workspace], CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(copy.Path, "later.txt"), "later", CancellationToken.None);
            var delivered = await courier.DeliverAsync(host, copy, false, CancellationToken.None);

            Assert.Equal([MutagenCourier.SessionOf(host, copy.Workspace)], rested);
            Assert.Empty(again);
            Assert.Null(delivered.Error);
            Assert.True(File.Exists(Path.Combine(host.Root, copy.Workspace.CopyFolder, "later.txt")));
        }
        finally
        {
            await courier.ForgetAsync(host, copy.Workspace, CancellationToken.None);
        }
    }

    /// <summary>A Mac's sshd has forty-two connections to give: past the limit the sessions used longest ago are the ones paused, one never delivered through first, and never the one just delivered through.</summary>
    [Fact]
    public void OverIn_MoreAwakeThanTheLimit_IsTheOnesUsedLongestAgoAndNeverTheOneKept()
    {
        var used = new Dictionary<string, DateTime>(StringComparer.Ordinal)
        {
            ["s3l-a"] = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc),
            ["s3l-b"] = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc),
            ["s3l-c"] = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc)
        };

        var over = MutagenCourier.OverIn(["s3l-a", "s3l-b", "s3l-c", "s3l-never"], "s3l-c", 2, name => used.GetValueOrDefault(name));

        Assert.Equal(["s3l-never", "s3l-b"], over);
        Assert.Empty(MutagenCourier.OverIn(["s3l-a", "s3l-b"], "s3l-a", 2, name => used.GetValueOrDefault(name)));
    }

    [Fact]
    public void BetaOf_AnotherHost_IsTheAddressAndTheCopyUnderItsRoot()
    {
        string beta = MutagenCourier.BetaOf(new Host("mini", "dev@mini"), new WorkspaceKey("Sample", "main"));

        Assert.Equal("dev@mini:~/.s3l-build/workspaces/Sample/main/Sample", beta);
    }
}
