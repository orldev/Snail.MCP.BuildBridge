using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;

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
            return Answer.Failed<Host>(Messages.NoHosts, Messages.NoHostsHint);
        if (string.IsNullOrWhiteSpace(name))
            return Answer.Ok(hosts[0]);

        var named = hosts.FirstOrDefault(host => string.Equals(host.Name, name, StringComparison.OrdinalIgnoreCase));

        return named is null
            ? Answer.Failed<Host>(Messages.UnknownHost(name, hosts.Select(host => host.Name)))
            : Answer.Ok(named);
    }

    /// <summary>Every host as it stands now, probed side by side.</summary>
    public async Task<IReadOnlyList<HostState>> ProbeAllAsync(CancellationToken cancellationToken)
    {
        var probes = All.Select(host => builder.ProbeAsync(host, cancellationToken));

        return await Task.WhenAll(probes);
    }

    /// <summary>The host a call means, probed and able to take a build of <paramref name="needs"/>; or why it cannot.</summary>
    public async Task<Answer<HostState>> ReadyAsync(string? name, Capability? needs, CancellationToken cancellationToken)
    {
        var named = Named(name);
        if (named.Value is not { } host)
            return Answer.Failed<HostState>(named.Error!, named.Hint);

        var state = await builder.ProbeAsync(host, cancellationToken);
        if (!state.IsReachable)
            return Answer.Failed<HostState>(state.Note ?? Messages.HostDown(host.Name, host.Address, "no answer"), Messages.HostDownHint);
        if (needs is { } capability && !state.Has(capability))
            return Answer.Failed<HostState>(Messages.HostCannot(host.Name, capability.ToString().ToLowerInvariant()));

        return Answer.Ok(state);
    }
}
