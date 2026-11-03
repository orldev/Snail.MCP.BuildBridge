using System.Collections;
using Microsoft.Extensions.Configuration;
using Snail.MCP.BuildBridge.Configuration;

namespace Snail.MCP.BuildBridge.Tests.Configuration;

public sealed class BuildSettingsTests
{
    [Fact]
    public void Read_HostsInVariables_BindToTheHostsInOrder()
    {
        var variables = new Hashtable
        {
            ["SNAIL_MCP_BUILDBRIDGE_HOSTS__0__NAME"] = "mini",
            ["SNAIL_MCP_BUILDBRIDGE_HOSTS__0__ADDRESS"] = "orldev@mini.local",
            ["SNAIL_MCP_BUILDBRIDGE_HOSTS__1__NAME"] = "studio",
            ["SNAIL_MCP_BUILDBRIDGE_HOSTS__1__ADDRESS"] = "studio",
            ["SNAIL_MCP_BUILDBRIDGE_HOSTS__1__ROOT"] = "/Volumes/Build/.s3l-build",
            ["HOSTS__0__NAME"] = "stranger"
        };

        var options = new BuildOptions();
        BuildSettings.Read(null, variables).Bind(options);
        var hosts = options.ToHosts();

        Assert.Equal(["mini", "studio"], hosts.Select(host => host.Name));
        Assert.Equal(Host.DefaultRoot, hosts[0].Root);
        Assert.Equal("/Volumes/Build/.s3l-build", hosts[1].Root);
    }

    [Fact]
    public void Read_NothingSet_KeepsTheDefaults()
    {
        var options = new BuildOptions();
        BuildSettings.Read(null, new Hashtable()).Bind(options);

        Assert.Empty(options.ToHosts());
        Assert.Equal(new SweepRules(), options.ToSweepRules());
        Assert.Contains("obj", options.Excludes);
    }
}
