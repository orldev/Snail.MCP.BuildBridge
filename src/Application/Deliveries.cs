using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>A working copy of this machine delivered to a host's workspace.</summary>
public sealed class Deliveries(Hosts hosts, ICopies copies, IBuilder builder)
{
    /// <summary>Delivers the copy <paramref name="path"/> lies in to the host named, or lists what a delivery would send.</summary>
    public async Task<Answer<Delivery>> DeliverAsync(string path, string? host, bool isDryRun, CancellationToken cancellationToken)
    {
        var copy = await copies.ReadAsync(path, cancellationToken);
        if (copy.Value is not { } found)
            return Answer.Failed<Delivery>(copy.Error!, copy.Hint);

        var ready = await hosts.ReadyAsync(host, null, cancellationToken);
        if (ready.Value is not { } state)
            return Answer.Failed<Delivery>(ready.Error!, ready.Hint);

        return await builder.DeliverAsync(state.Host, found, isDryRun, cancellationToken);
    }
}
