using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>A host made ready to build: its root, the SDK and workloads the settings name, the feeds, and what a person still has to install.</summary>
public sealed class Provisioning(Hosts hosts, IBuilder builder, Logs logs, IOptions<BuildOptions> options)
{
    /// <summary>Provisions the host named, or the first; a host that does not answer is not provisioned.</summary>
    public async Task<Answer<Provisioned>> RunAsync(string? host, CancellationToken cancellationToken)
    {
        var ready = await hosts.ReadyAsync(host, [], cancellationToken);
        if (ready.Value is not { } state)
            return Answer.Failed<Provisioned>(ready.Error!, ready.Hint);

        string log = logs.New("provision");
        var timeout = TimeSpan.FromSeconds(options.Value.RunSeconds);

        return await builder.ProvisionAsync(state.Host, options.Value.ToManifest(), log, timeout, cancellationToken);
    }
}
