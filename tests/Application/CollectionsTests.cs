using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.BuildBridge.Tests.Application;

public sealed class CollectionsTests
{
    [Fact]
    public async Task FetchAsync_NoPathNamed_IsRefusedAsArgumentsBeforeTouchingTheHost()
    {
        var builder = new FakeBuilder();
        var options = Options.Create(new BuildOptions { Hosts = [new HostSetting { Name = "mini", Address = "mini.local" }] });
        var collections = new Collections(new Hosts(options, builder), new FakeCopies(), builder);

        var answer = await collections.FetchAsync("/Users/me/Sample", null, [], null, CancellationToken.None);

        Assert.Equal(Messages.NothingToFetch, answer.Error);
        Assert.Equal(ToolFailures.Arguments, answer.Type);
        Assert.Empty(builder.Probed);
    }
}
