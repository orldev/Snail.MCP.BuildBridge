using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Tests.Application;

/// <summary>A builder that answers at once with what the test sets, and remembers what it was asked.</summary>
internal sealed class FakeBuilder : IBuilder
{
    public List<Host> Probed { get; } = [];

    public List<(Host Host, LocalCopy Copy, bool IsDryRun)> Delivered { get; } = [];

    /// <summary>The hosts that answer; every other is unreachable.</summary>
    public Dictionary<string, HostState> Up { get; } = new(StringComparer.Ordinal);

    public Task<HostState> ProbeAsync(Host host, CancellationToken cancellationToken)
    {
        Probed.Add(host);

        return Task.FromResult(Up.GetValueOrDefault(host.Name) ?? HostState.Unreachable(host, $"{host.Name} is down"));
    }

    public Task<Answer<Provisioned>> ProvisionAsync(Host host, BuilderManifest manifest, string log, TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult(Answer.Ok(new Provisioned(host, [new ProvisionStep("folders", true, host.Root)], log)));

    public Task<Answer<Delivery>> DeliverAsync(Host host, LocalCopy copy, bool isDryRun, CancellationToken cancellationToken)
    {
        Delivered.Add((host, copy, isDryRun));

        return Task.FromResult(Answer.Ok(new Delivery(copy.Workspace, ["README.md"], 12, TimeSpan.FromSeconds(1), isDryRun)));
    }

    public List<(string Source, string Relative)> Mirrored { get; } = [];

    public List<(WorkspaceKey Key, IReadOnlyList<string> Command)> Executed { get; } = [];

    /// <summary>What a run writes into its log before it ends.</summary>
    public string LogText { get; set; } = "Passed!  - Failed:     0, Passed:    12, Skipped:     1, Total:    13, Duration: 2 s - Sample.Tests.dll (net10.0)\n";

    /// <summary>The exit code a run ends with.</summary>
    public int ExitCode { get; set; }

    /// <summary>What a run waits for before it ends; done already unless a test holds it.</summary>
    public Task Held { get; set; } = Task.CompletedTask;

    public Task<Answer<Delivery>> MirrorAsync(Host host, string source, string relative, CancellationToken cancellationToken)
    {
        Mirrored.Add((source, relative));

        return Task.FromResult(Answer.Ok(new Delivery(new WorkspaceKey(relative, string.Empty), [], 0, TimeSpan.Zero, false)));
    }

    /// <summary>The environment each execution was given, in order.</summary>
    public List<IReadOnlyDictionary<string, string>> Environments { get; } = [];

    public async Task<Answer<Execution>> ExecuteAsync(Host host, WorkspaceKey key, IReadOnlyList<string> command, IReadOnlyDictionary<string, string> environment, string log, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Executed.Add((key, command));
        Environments.Add(environment);
        await Held.WaitAsync(cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        await File.WriteAllTextAsync(log, LogText, cancellationToken);

        return Answer.Ok(new Execution(ExitCode, log, TimeSpan.FromSeconds(3), false));
    }

    public List<(string Device, string Clone)> Acquired { get; } = [];

    public List<string> Released { get; } = [];

    /// <summary>How many clones the host gives before refusing; every one unless a test says otherwise.</summary>
    public int SimulatorsAvailable { get; set; } = int.MaxValue;

    public Task<Answer<string>> AcquireSimulatorAsync(Host host, string device, string clone, CancellationToken cancellationToken)
    {
        Acquired.Add((device, clone));

        return Task.FromResult(Acquired.Count > SimulatorsAvailable ? Answer.Failed<string>($"{host.Name} has no room for another simulator") : Answer.Ok($"UDID-{Acquired.Count}"));
    }

    public Task ReleaseSimulatorAsync(Host host, string udid, CancellationToken cancellationToken)
    {
        Released.Add(udid);

        return Task.CompletedTask;
    }

    public Task<Answer<Collected>> CollectAsync(Host host, WorkspaceKey key, IReadOnlyList<string> paths, string folder, CancellationToken cancellationToken) =>
        Task.FromResult(Answer.Ok(new Collected(folder, [], 0)));

    public Task<Answer<bool>> RemoveAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken) => Task.FromResult(Answer.Ok(true));

    /// <summary>Each supply asked for, in order.</summary>
    public List<Supply> Supplied { get; } = [];

    /// <summary>Why a supply fails, when it does.</summary>
    public string? SupplyError { get; set; }

    public Task<Answer<IReadOnlyList<string>>> SupplyAsync(Host host, Supply supply, CancellationToken cancellationToken)
    {
        Supplied.Add(supply);

        return Task.FromResult(SupplyError is { } error ? Answer.Failed<IReadOnlyList<string>>(error) : Answer.Ok(supply.Words));
    }

    /// <summary>The sessions whose copy is gone.</summary>
    public List<string> Stranded { get; } = [];

    public Task<IReadOnlyList<string>> StrandedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Stranded);

    /// <summary>What a survey of any host finds.</summary>
    public Survey? Found { get; set; }

    public List<SweepItem> CarriedOut { get; } = [];

    public Task<Answer<Survey>> SurveyAsync(Host host, CancellationToken cancellationToken) =>
        Task.FromResult(Answer.Ok(Found ?? new Survey(host, [], [], [], 0, 0, 400)));

    public Task<Answer<bool>> CarryOutAsync(Host host, SweepItem item, CancellationToken cancellationToken)
    {
        CarriedOut.Add(item);

        return Task.FromResult(Answer.Ok(true));
    }
}

/// <summary>Copies that are what the test says they are, whatever the path.</summary>
internal sealed class FakeCopies : ICopies
{
    public LocalCopy? Copy { get; set; } = new("/Users/me/Sample", "Sample", "main", "abc1234", false);

    public Task<Answer<LocalCopy>> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Copy is { } copy ? Answer.Ok(copy) : Answer.Failed<LocalCopy>(Messages.NotACopy(path), type: Refusals.NotACopy));

    public Task<IReadOnlyList<string>> IgnoredAsync(LocalCopy copy, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> IgnorePatternsAsync(LocalCopy copy, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>Secrets that are what the test sets, for every repository, or a failure to read them.</summary>
internal sealed class FakeSecrets : ISecrets
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public string? Error { get; set; }

    public List<string> AskedFor { get; } = [];

    public Task<Answer<IReadOnlyDictionary<string, string>>> ForAsync(string repository, CancellationToken cancellationToken)
    {
        AskedFor.Add(repository);

        return Task.FromResult(Error is { } error ? Answer.Failed<IReadOnlyDictionary<string, string>>(error) : Answer.Ok<IReadOnlyDictionary<string, string>>(Values));
    }
}
