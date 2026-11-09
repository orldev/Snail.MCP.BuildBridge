using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>A working copy of this machine delivered to a host's workspace.</summary>
/// <remarks>A delivery takes the workspace's turn like a run does, so it never pulls files from under a build; a dry run changes nothing and waits for nobody.</remarks>
public sealed class Deliveries(Hosts hosts, ICopies copies, IBuilder builder, WorkspaceTurns turns)
{
    /// <summary>Delivers the copy <paramref name="path"/> lies in to the host named, or lists what a delivery would send.</summary>
    public async Task<Answer<Delivery>> DeliverAsync(string path, string? host, bool isDryRun, CancellationToken cancellationToken)
    {
        var copy = await copies.ReadAsync(path, cancellationToken);
        if (!copy.TryGet(out var found))
            return copy.As<Delivery>();

        var ready = await hosts.ReadyAsync(host, [], cancellationToken);
        if (!ready.TryGet(out var state))
            return ready.As<Delivery>();

        if (isDryRun)
            return await builder.DeliverAsync(state.Host, found, true, cancellationToken);

        using (await turns.TakeAsync(state.Host, found.Workspace, () => { }, cancellationToken))
            return await builder.DeliverAsync(state.Host, found, false, cancellationToken);
    }
}
