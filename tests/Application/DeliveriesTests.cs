using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;

namespace Snail.MCP.BuildBridge.Tests.Application;

public sealed class DeliveriesTests
{
    private static readonly Host Mini = new("mini", "mini.local");

    private static (Deliveries Deliveries, FakeBuilder Builder, FakeCopies Copies) Of()
    {
        var builder = new FakeBuilder();
        builder.Up[Mini.Name] = new HostState(Mini, true, [], 400, null);
        var copies = new FakeCopies();
        var hosts = new Hosts(Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = Mini.Name, Address = Mini.Address }] }), builder);

        return (new Deliveries(hosts, copies, builder), builder, copies);
    }

    [Fact]
    public async Task DeliverAsync_ACopy_GoesToTheHostNamedOrTheFirst()
    {
        var (deliveries, builder, _) = Of();

        var answer = await deliveries.DeliverAsync("/Users/me/Sample/src", null, true, CancellationToken.None);

        Assert.NotNull(answer.Value);
        var (host, copy, isDryRun) = Assert.Single(builder.Delivered);
        Assert.Equal(Mini, host);
        Assert.Equal("Sample", copy.Repository);
        Assert.True(isDryRun);
    }

    [Fact]
    public async Task DeliverAsync_PathOutsideACopy_DeliversNothingAndSaysWhy()
    {
        var (deliveries, builder, copies) = Of();
        copies.Copy = null;

        var answer = await deliveries.DeliverAsync("/Users/me/elsewhere", null, false, CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Contains("not inside a git working copy", answer.Error, StringComparison.Ordinal);
        Assert.Empty(builder.Delivered);
    }
}
