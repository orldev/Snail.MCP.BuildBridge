using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>A copy kept mirrored into its workspace by a Mutagen session, one per workspace and host, so a delivery is only waiting for the session to catch up.</summary>
/// <remarks>
/// <para>
/// The session is one-way: the workspace is a replica of the copy, and what a build writes there under an ignored
/// folder stays, as it does with rsync. It ignores what rsync would exclude and every pattern of the copy's .gitignore
/// files, so a folder the host makes for itself under an ignored path that does not exist here is kept too. Since the
/// patterns are fixed at creation, a label on the session carries a hash of them with the copy's path: a session whose
/// label no longer matches is terminated and made again, so an ignore added to .gitignore reaches the host.
/// </para>
/// <para>
/// Mutagen runs its own daemon on this machine, started on the first command and kept alive between runs of this
/// server; that is what makes the sync continuous. A dry run goes through rsync, which lists the difference between
/// the copy and the workspace whichever means keeps them alike.
/// </para>
/// </remarks>
public sealed class MutagenCourier(IPrograms programs, ICopies copies, RsyncCourier rsync, RemoteShell shell, IOptions<BuildOptions> options, TimeProvider clock) : ICourier
{
    /// <summary>The label on a session carrying the hash of what shaped it.</summary>
    public const string ShapeLabel = "s3l.shape";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    public async Task<Answer<Delivery>> DeliverAsync(Host host, LocalCopy copy, bool isDryRun, CancellationToken cancellationToken)
    {
        if (isDryRun)
            return await rsync.DeliverAsync(host, copy, true, cancellationToken);

        var key = copy.Workspace;
        var words = shell.Command(host, RemoteShell.Prelude(host) + $"mkdir -p {RemoteShell.Under(host, key.CopyFolder)}");
        var made = await programs.RunAsync(words[0], words[1..], null, TimeSpan.FromSeconds(options.Value.ProbeSeconds), cancellationToken);
        if (!made.IsSuccess)
            return Answer.Failed<Delivery>(Messages.DeliveryFailed(host.Name, Rsync.Reason(made)));

        var ignored = await copies.IgnoredAsync(copy, cancellationToken);
        var gitignore = await copies.IgnorePatternsAsync(copy, cancellationToken);
        var patterns = rsync.Excludes(copy, ignored).Concat(gitignore).Distinct(StringComparer.Ordinal).ToList();
        string session = SessionOf(host, key);
        string beta = BetaOf(host, key);
        string shape = ShapeOf(copy.Path, beta, patterns);
        var started = clock.GetTimestamp();

        var found = await SessionAsync(session, cancellationToken);
        if (found.IsMissing)
            return Answer.Failed<Delivery>(Messages.ProgramMissing(options.Value.Mutagen), Messages.InstallMutagen);
        if (found.Shape is not null && found.Shape != shape)
            await TerminateAsync(session, cancellationToken);
        if (found.Shape != shape)
        {
            var created = await CreateAsync(session, shape, copy.Path, beta, patterns, cancellationToken);
            if (!created.IsSuccess)
                return Answer.Failed<Delivery>(Messages.SessionFailed(session, Rsync.Reason(created)));
        }

        var flushed = await programs.RunAsync(options.Value.Mutagen, ["sync", "flush", session], null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken);
        if (!flushed.IsSuccess)
            return Answer.Failed<Delivery>(Messages.SessionFailed(session, Rsync.Reason(flushed)));

        return Answer.Ok(new Delivery(key, [], 0, clock.GetElapsedTime(started), false, Delivery.Mutagen));
    }

    /// <inheritdoc/>
    public async Task ForgetAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken) =>
        await TerminateAsync(SessionOf(host, key), cancellationToken);

    /// <summary>The session's name for a workspace on a host: letters, digits and dashes, as Mutagen names sessions, cut to its 64 with a hash of the whole so two long names never share one.</summary>
    public static string SessionOf(Host host, WorkspaceKey key)
    {
        string raw = $"s3l-{key.Repository}-{key.Branch}-{host.Name}".ToLowerInvariant();
        var letters = raw.Select(letter => char.IsAsciiLetterOrDigit(letter) ? letter : '-');
        string name = new([.. letters]);
        while (name.Contains("--", StringComparison.Ordinal))
            name = name.Replace("--", "-", StringComparison.Ordinal);
        name = name.TrimEnd('-');
        if (name.Length <= 64)
            return name;

        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..8];

        return $"{name[..55].TrimEnd('-')}-{hash}";
    }

    /// <summary>The hash of what shapes a session: where it reads, where it writes and what it ignores; a session with another hash is made again.</summary>
    public static string ShapeOf(string alpha, string beta, IReadOnlyList<string> patterns)
    {
        string text = string.Join('\n', [alpha, beta, .. patterns]);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return Convert.ToHexStringLower(hash)[..16];
    }

    /// <summary>The copy in the workspace as Mutagen names it: a path on this machine, or <c>address:path</c> on another.</summary>
    public static string BetaOf(Host host, WorkspaceKey key) =>
        host.IsLocal ? Path.Combine(RemoteShell.LocalRoot(host), key.CopyFolder) : $"{host.Address}:{host.Root}/{key.CopyFolder}";

    private async Task<(bool IsMissing, string? Shape)> SessionAsync(string session, CancellationToken cancellationToken)
    {
        var run = await programs.RunAsync(options.Value.Mutagen, ["sync", "list", "--template", "{{json .}}", session], null, Patience, cancellationToken);
        if (run.IsMissing)
            return (true, null);
        if (!run.IsSuccess)
            return (false, null);

        return (false, ShapeIn(run.Output));
    }

    /// <summary>The shape label of the first session in Mutagen's JSON, or null when there is none.</summary>
    internal static string? ShapeIn(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            return null;

        var first = document.RootElement[0];
        if (!first.TryGetProperty("labels", out var labels) || !labels.TryGetProperty(ShapeLabel, out var shape))
            return string.Empty;

        return shape.GetString() ?? string.Empty;
    }

    private Task<ProgramRun> CreateAsync(string session, string shape, string alpha, string beta, IReadOnlyList<string> patterns, CancellationToken cancellationToken)
    {
        List<string> arguments =
        [
            "sync", "create", "--name", session, "--label", $"{ShapeLabel}={shape}",
            "--mode", "one-way-replica", "--no-ignore-vcs", "--symlink-mode", "posix-raw"
        ];
        foreach (var pattern in patterns)
            arguments.AddRange(["--ignore", pattern]);
        arguments.AddRange([alpha, beta]);

        return programs.RunAsync(options.Value.Mutagen, arguments, null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken);
    }

    private Task TerminateAsync(string session, CancellationToken cancellationToken) =>
        programs.RunAsync(options.Value.Mutagen, ["sync", "terminate", session], null, Patience, cancellationToken);
}
