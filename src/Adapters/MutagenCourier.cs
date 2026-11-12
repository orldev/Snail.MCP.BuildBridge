using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Answers;

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
/// <para>
/// A session holds a connection to its host while it lives. One whose workspace has gone unused is paused by the
/// sweep after a run and resumed by its next delivery, so the connections held are those of the copies being worked on.
/// </para>
/// </remarks>
public sealed class MutagenCourier(IPrograms programs, ICopies copies, RsyncCourier rsync, RemoteShell shell, IOptions<BuildOptions> options, TimeProvider clock, string? folder = null) : ICourier
{
    /// <summary>The folder a file per session lies in, written at each delivery: its time is when the session was last used, which Mutagen does not tell.</summary>
    public string Folder { get; } = folder ?? Path.Combine(ServerFolders.DataDirectory(BuildSettings.Name), "sessions");

    /// <summary>What every session's name of this server starts with.</summary>
    public const string SessionPrefix = "s3l-";

    /// <summary>The label on a session carrying the hash of what shaped it.</summary>
    public const string ShapeLabel = "s3l.shape";

    /// <summary>What Mutagen says of a flush asked of a paused session.</summary>
    private const string PausedWords = "session is paused";

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
            return Answer.Failed(Messages.DeliveryFailed(host.Name, Rsync.Reason(made)), type: Refusals.NotDelivered);

        var ignored = await copies.IgnoredAsync(copy, cancellationToken);
        var gitignore = await copies.IgnorePatternsAsync(copy, cancellationToken);
        var patterns = rsync.Excludes(copy, ignored).Concat(gitignore).Distinct(StringComparer.Ordinal).ToList();
        string session = SessionOf(host, key);
        string beta = BetaOf(host, key);
        string shape = ShapeOf(copy.Path, beta, patterns);
        var started = clock.GetTimestamp();

        var found = await SessionAsync(session, cancellationToken);
        if (found.IsMissing)
            return Answer.Failed(Messages.ProgramMissing(options.Value.Mutagen), Messages.InstallMutagen, Refusals.NotInstalled);
        if (found.Shape is not null && found.Shape != shape)
            await TerminateAsync(session, cancellationToken);
        if (found.Shape != shape)
        {
            var created = await CreateAsync(session, shape, copy.Path, beta, patterns, cancellationToken);
            if (!created.IsSuccess)
                return Answer.Failed(Messages.SessionFailed(session, Rsync.Reason(created)), type: Refusals.NotDelivered);
        }

        var flushed = await FlushAsync(session, cancellationToken);
        if (!flushed.IsSuccess)
            return Answer.Failed(Messages.SessionFailed(session, Rsync.Reason(flushed)), type: Refusals.NotDelivered);

        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Path.Combine(Folder, session), [], cancellationToken);
        await KeepWithinAsync(session, cancellationToken);

        return Answer.Ok(new Delivery(key, [], 0, clock.GetElapsedTime(started), false, Delivery.Mutagen));
    }

    /// <inheritdoc/>
    public async Task ForgetAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken) =>
        await TerminateAsync(SessionOf(host, key), cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// A session outlives the copy it mirrors: a worktree removed or a scratch folder erased leaves it connected to the
    /// host for ever, and the host's sshd takes 42 connections at once — on 2026-11-12 eleven of 57 sessions on this
    /// machine mirrored a folder that was gone. Only sessions carrying this server's label are looked at.
    /// </remarks>
    public async Task<IReadOnlyList<string>> StrandedAsync(CancellationToken cancellationToken)
    {
        var run = await ListAsync(cancellationToken);

        return run.IsSuccess ? StrandedIn(run.Output, Directory.Exists) : [];
    }

    /// <inheritdoc/>
    /// <remarks>Every name goes to one <c>mutagen sync pause</c>; a session already paused is not named again, so a sweep reports a session once.</remarks>
    public async Task<IReadOnlyList<string>> RestAsync(Host host, IReadOnlyList<WorkspaceKey> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
            return [];

        var run = await ListAsync(cancellationToken);
        if (!run.IsSuccess)
            return [];

        var idle = keys.Select(key => SessionOf(host, key)).ToHashSet(StringComparer.Ordinal);

        return await PauseAsync([.. AwakeIn(run.Output).Where(idle.Contains)], cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// launchd gives a Mac's sshd forty-two connections, and sessions that are awake can hold every one of them: on
    /// 2026-11-12 forty-two of forty-six did, four more kept asking, and the host closed every new connection for as
    /// long as it was watched — five minutes — until the sessions were paused by hand. A session resumes with its
    /// next delivery, so pausing all of them costs the next run of each two seconds.
    /// </remarks>
    public async Task<IReadOnlyList<string>> RestAsync(CancellationToken cancellationToken)
    {
        var run = await ListAsync(cancellationToken);

        return run.IsSuccess ? await PauseAsync(AwakeIn(run.Output), cancellationToken) : [];
    }

    /// <inheritdoc/>
    public async Task EndAsync(string name, CancellationToken cancellationToken)
    {
        if (name.StartsWith(SessionPrefix, StringComparison.Ordinal))
            await TerminateAsync(name, cancellationToken);
    }

    /// <summary>Pauses the sessions used longest ago when more are awake than the settings allow, the one just delivered through never among them.</summary>
    /// <remarks>
    /// launchd gives a Mac's sshd forty-two connections and every session awake holds one. Thirty workspaces worked on
    /// within half an hour are rare, forty-three would shut the host to everything: the limit keeps a dozen connections
    /// for runs, and the session it pauses resumes with its next delivery.
    /// </remarks>
    private async Task KeepWithinAsync(string session, CancellationToken cancellationToken)
    {
        if (options.Value.SessionsAwake <= 0)
            return;

        var run = await ListAsync(cancellationToken);
        if (!run.IsSuccess)
            return;

        var over = OverIn(AwakeIn(run.Output), session, options.Value.SessionsAwake, name => File.GetLastWriteTimeUtc(Path.Combine(Folder, name)));
        await PauseAsync(over, cancellationToken);
    }

    /// <summary>The sessions of <paramref name="awake"/> to pause so that no more than <paramref name="most"/> stay awake: those used longest ago by <paramref name="lastUsed"/>, never <paramref name="kept"/>.</summary>
    internal static IReadOnlyList<string> OverIn(IReadOnlyList<string> awake, string kept, int most, Func<string, DateTime> lastUsed)
    {
        int over = awake.Count - most;
        if (over <= 0)
            return [];

        return [.. awake.Where(name => name != kept).OrderBy(lastUsed).Take(over)];
    }

    /// <summary>The names of this server's sessions in Mutagen's JSON that are not paused.</summary>
    internal static IReadOnlyList<string> AwakeIn(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var awake = new List<string>();
        foreach (var session in document.RootElement.EnumerateArray())
        {
            string? name = session.TryGetProperty("name", out var named) ? named.GetString() : null;
            bool isPaused = session.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True;
            if (name is not null && name.StartsWith(SessionPrefix, StringComparison.Ordinal) && !isPaused)
                awake.Add(name);
        }

        return awake;
    }

    /// <summary>The names of the sessions in Mutagen's JSON whose copy on this machine <paramref name="isThere"/> says is gone.</summary>
    internal static IReadOnlyList<string> StrandedIn(string json, Func<string, bool> isThere)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var stranded = new List<string>();
        foreach (var session in document.RootElement.EnumerateArray())
        {
            string? name = session.TryGetProperty("name", out var named) ? named.GetString() : null;
            string? copy = session.TryGetProperty("alpha", out var alpha) && alpha.TryGetProperty("path", out var path) ? path.GetString() : null;
            if (name is not null && copy is not null && name.StartsWith(SessionPrefix, StringComparison.Ordinal) && !isThere(copy))
                stranded.Add(name);
        }

        return stranded;
    }

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

    private async Task<IReadOnlyList<string>> PauseAsync(IReadOnlyList<string> sessions, CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
            return [];

        var paused = await programs.RunAsync(options.Value.Mutagen, ["sync", "pause", .. sessions], null, TimeSpan.FromSeconds(options.Value.TransferSeconds), cancellationToken);

        return paused.IsSuccess ? sessions : [];
    }

    private Task<ProgramRun> ListAsync(CancellationToken cancellationToken) =>
        programs.RunAsync(options.Value.Mutagen, ["sync", "list", "--label-selector", ShapeLabel, "--template", "{{json .}}"], null, Patience, cancellationToken);

    /// <summary>Has the session catch up; one that was put to rest is resumed first, which waits for it to connect — two seconds to a host over ssh (2026-11-12).</summary>
    private async Task<ProgramRun> FlushAsync(string session, CancellationToken cancellationToken)
    {
        var patience = TimeSpan.FromSeconds(options.Value.TransferSeconds);
        var flushed = await programs.RunAsync(options.Value.Mutagen, ["sync", "flush", session], null, patience, cancellationToken);
        if (flushed.IsSuccess || !flushed.Error.Contains(PausedWords, StringComparison.Ordinal))
            return flushed;

        var resumed = await programs.RunAsync(options.Value.Mutagen, ["sync", "resume", session], null, patience, cancellationToken);
        if (!resumed.IsSuccess)
            return resumed;

        return await programs.RunAsync(options.Value.Mutagen, ["sync", "flush", session], null, patience, cancellationToken);
    }

    private Task TerminateAsync(string session, CancellationToken cancellationToken) =>
        programs.RunAsync(options.Value.Mutagen, ["sync", "terminate", session], null, Patience, cancellationToken);
}
