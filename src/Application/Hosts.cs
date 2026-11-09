using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>The hosts the settings name: which one a call means, and whether it can take a build.</summary>
public sealed class Hosts(IOptions<BuildOptions> options, IBuilder builder)
{
    /// <summary>Every host the settings name, in their order.</summary>
    public IReadOnlyList<Host> All => options.Value.ToHosts();

    /// <summary>The host a call means: the one named, or the first when none is; or why there is none.</summary>
    public Answer<Host> Named(string? name)
    {
        var hosts = All;
        if (hosts.Count == 0)
            return Answer.Failed(Messages.NoHosts, Messages.NoHostsHint, Refusals.NoHosts);
        if (string.IsNullOrWhiteSpace(name))
            return Answer.Ok(hosts[0]);

        var named = hosts.FirstOrDefault(host => string.Equals(host.Name, name, StringComparison.OrdinalIgnoreCase));

        return named is null
            ? Answer.Failed(Messages.UnknownHost(name, hosts.Select(host => host.Name)), type: Refusals.NoSuchHost)
            : Answer.Ok(named);
    }

    /// <summary>Every host as it stands now, probed side by side.</summary>
    public async Task<IReadOnlyList<HostState>> ProbeAllAsync(CancellationToken cancellationToken)
    {
        var probes = All.Select(host => builder.ProbeAsync(host, cancellationToken));

        return await Task.WhenAll(probes);
    }

    /// <summary>The host a call means, probed and able to take a build with any one of <paramref name="needs"/>; or why it cannot.</summary>
    public async Task<Answer<HostState>> ReadyAsync(string? name, IReadOnlyList<Capability> needs, CancellationToken cancellationToken)
    {
        var named = Named(name);
        if (!named.TryGet(out var host))
            return named.As<HostState>();

        var state = await builder.ProbeAsync(host, cancellationToken);
        if (!state.IsReachable)
            return Answer.Failed(state.Note ?? Messages.HostDown(host.Name, host.Address, "no answer"), Messages.HostDownHint, Refusals.Unreachable);
        if (needs.Count > 0 && !needs.Any(state.Has))
            return Answer.Failed(Messages.HostCannot(host.Name, string.Join(" or ", needs.Select(capability => capability.ToString().ToLowerInvariant()))), type: Refusals.NoToolchain);

        return Answer.Ok(state);
    }
}
