using System.Collections.Concurrent;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Application;

/// <summary>One run or delivery per workspace at a time: the turn a caller takes before touching a workspace, and gives back when done.</summary>
/// <remarks>
/// Two sessions on one branch would otherwise write into one <c>obj</c>, and a delivery under a running build would
/// pull files from under it — whichever tool the delivery came through, so the turn is one object for every caller.
/// </remarks>
public sealed class WorkspaceTurns
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _turns = new(StringComparer.Ordinal);

    /// <summary>Waits for the turn of <paramref name="key"/> on <paramref name="host"/>; <paramref name="isWaiting"/> is told when another holds it now.</summary>
    public Task<IDisposable> TakeAsync(Host host, WorkspaceKey key, Action isWaiting, CancellationToken cancellationToken) =>
        TakeAsync(host, key.Folder, isWaiting, cancellationToken);

    /// <summary>Waits for the turn of <paramref name="what"/> on <paramref name="host"/> — a workspace's folder, or another thing of the host's one caller may touch at a time; <paramref name="isWaiting"/> is told when another holds it now.</summary>
    public async Task<IDisposable> TakeAsync(Host host, string what, Action isWaiting, CancellationToken cancellationToken)
    {
        var turn = _turns.GetOrAdd($"{host.Name}/{what}", _ => new SemaphoreSlim(1, 1));
        if (!await turn.WaitAsync(0, cancellationToken))
        {
            isWaiting();
            await turn.WaitAsync(cancellationToken);
        }

        return new Taken(turn);
    }

    private sealed class Taken(SemaphoreSlim turn) : IDisposable
    {
        public void Dispose() => turn.Release();
    }
}
