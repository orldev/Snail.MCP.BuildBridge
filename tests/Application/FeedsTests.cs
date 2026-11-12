using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Application;

/// <summary>A feed is mirrored to a host when it changed, not before every run.</summary>
public sealed class FeedsTests
{
    private static readonly Host Mini = new("mini", "mini.local");

    private static (Feeds Feeds, FakeBuilder Builder, string Folder) Of()
    {
        string folder = TestFolder.Create();
        File.WriteAllText(Path.Combine(folder, "Sample.1.0.0.nupkg"), "one");
        var builder = new FakeBuilder();
        var options = Options.Create(new BuildOptions { Feeds = [new FeedSetting { Name = "LocalPackages", Path = folder }] });

        return (new Feeds(builder, new Logs(TimeProvider.System, TestFolder.Create()), options, TimeProvider.System), builder, folder);
    }

    [Fact]
    public async Task MirrorAsync_AFeedThatDidNotChange_IsSentOnce()
    {
        var (feeds, builder, _) = Of();

        await feeds.MirrorAsync(Mini, CancellationToken.None);
        var again = await feeds.MirrorAsync(Mini, CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Single(builder.Mirrored);
    }

    [Fact]
    public async Task MirrorAsync_APackageAddedToTheFeed_IsSentAgain()
    {
        var (feeds, builder, folder) = Of();

        await feeds.MirrorAsync(Mini, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(folder, "Sample.1.1.0.nupkg"), "two", CancellationToken.None);
        await feeds.MirrorAsync(Mini, CancellationToken.None);

        Assert.Equal(2, builder.Mirrored.Count);
    }

    /// <summary>Each host has its own copy of a feed, so one host being up to date says nothing of another.</summary>
    [Fact]
    public async Task MirrorAsync_AnotherHost_GetsTheFeedOfItsOwn()
    {
        var (feeds, builder, _) = Of();

        await feeds.MirrorAsync(Mini, CancellationToken.None);
        await feeds.MirrorAsync(new Host("studio", "studio.local"), CancellationToken.None);

        Assert.Equal(2, builder.Mirrored.Count);
    }
}
