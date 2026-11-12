using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tests.Application;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

/// <summary>Each way the work on a host can fail, answered with a type of its own: a job that could not happen once ended <c>failed</c>, whatever stopped it.</summary>
public sealed class RefusalTypesTests
{
    /// <summary>Programs that end as the test says, by the program's name; every one written to its log as it would be.</summary>
    private sealed class ScriptedPrograms(Func<string, ProgramRun> ending) : IPrograms
    {
        public Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, string? directory, TimeSpan timeout, CancellationToken cancellationToken, string? input = null) =>
            Task.FromResult(ending(program));

        public async Task<ProgramRun> RunToLogAsync(string program, IReadOnlyList<string> arguments, string? directory, string log, TimeSpan timeout, CancellationToken cancellationToken, string? input = null)
        {
            var run = ending(program);
            await File.WriteAllTextAsync(log, run.Output, cancellationToken);

            return run;
        }
    }

    private static readonly IOptions<BuildOptions> Settings = Options.Create(new BuildOptions());

    private static readonly Host Mini = new("mini", "mini.local");

    private static readonly LocalCopy Copy = new("/Users/me/Sample", "Sample", "main", "abc1234", false);

    private static readonly ProgramRun Ended = new(0, string.Empty, string.Empty);

    private static readonly BuilderManifest Manifest = new("10.0.302", [], [], []);

    private static readonly ProgramRun Broken = new(255, string.Empty, "Connection reset by peer");

    private static SshBuilder Builder(Func<string, ProgramRun> ending)
    {
        var programs = new ScriptedPrograms(ending);
        var shell = new RemoteShell(Settings);

        return new SshBuilder(programs, new RsyncCourier(programs, new FakeCopies(), shell, Settings, TimeProvider.System), shell, Settings, TimeProvider.System);
    }

    /// <summary>A courier whose sessions hold the host's connections until they are put to rest.</summary>
    private sealed class CrowdingCourier : ICourier
    {
        public bool IsResting { get; private set; }

        public Task<Answer<Delivery>> DeliverAsync(Host host, LocalCopy copy, bool isDryRun, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ForgetAsync(Host host, WorkspaceKey key, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> StrandedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> RestAsync(Host host, IReadOnlyList<WorkspaceKey> keys, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> RestAsync(CancellationToken cancellationToken)
        {
            IsResting = true;

            return Task.FromResult<IReadOnlyList<string>>(["s3l-sample-main-mini"]);
        }

        public Task EndAsync(string name, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// A Mac's sshd has forty-two connections to give, and this machine's sessions can hold them all: a host that closes
    /// the connection before its greeting is asked again once the sessions were put to rest, and answers.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_AHostWhoseConnectionsTheSessionsHold_AnswersOnceTheyRest()
    {
        var courier = new CrowdingCourier();
        var programs = new ScriptedPrograms(_ => courier.IsResting ? new ProgramRun(0, "home=/Users/dev\nfree=100\n", string.Empty) : new ProgramRun(255, string.Empty, "Connection closed by 192.0.2.10 port 22"));
        var builder = new SshBuilder(programs, courier, new RemoteShell(Settings), Settings, TimeProvider.System);

        var state = await builder.ProbeAsync(Mini, CancellationToken.None);

        Assert.True(courier.IsResting);
        Assert.True(state.IsReachable);
    }

    private static string Log() => Path.Combine(TestFolder.Create(), "provision.log");

    [Fact]
    public async Task ProvisionAsync_SshNotOnThisMachine_IsNotInstalled()
    {
        var answer = await Builder(ProgramRun.Missing).ProvisionAsync(Mini, Manifest, Log(), TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal("notInstalled", answer.Type);
    }

    [Fact]
    public async Task ProvisionAsync_PastItsTime_IsTimeout()
    {
        var answer = await Builder(_ => new ProgramRun(-1, string.Empty, string.Empty, IsTimedOut: true)).ProvisionAsync(Mini, Manifest, Log(), TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal("timeout", answer.Type);
    }

    [Fact]
    public async Task ProvisionAsync_BrokenOffBeforeAnyStep_IsNotProvisioned()
    {
        var answer = await Builder(_ => Broken).ProvisionAsync(Mini, Manifest, Log(), TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal("notProvisioned", answer.Type);
    }

    [Fact]
    public async Task DeliverAsync_RsyncNotOnThisMachine_IsNotInstalled()
    {
        var answer = await Builder(program => program == Settings.Value.Rsync ? ProgramRun.Missing(program) : Ended).DeliverAsync(Mini, Copy, false, CancellationToken.None);

        Assert.Equal("notInstalled", answer.Type);
    }

    [Fact]
    public async Task DeliverAsync_TransferBrokenOff_IsNotDelivered()
    {
        var answer = await Builder(program => program == Settings.Value.Rsync ? Broken : Ended).DeliverAsync(Mini, Copy, false, CancellationToken.None);

        Assert.Equal("notDelivered", answer.Type);
    }

    [Fact]
    public async Task MirrorAsync_HostRefusingTheFolder_IsNotDelivered()
    {
        var answer = await Builder(_ => Broken).MirrorAsync(Mini, "/feeds/local", "feeds/local", CancellationToken.None);

        Assert.Equal("notDelivered", answer.Type);
    }

    [Fact]
    public async Task CollectAsync_TransferBrokenOff_IsNotCollected()
    {
        var answer = await Builder(_ => Broken).CollectAsync(Mini, Copy.Workspace, ["artifacts"], TestFolder.Create(), CancellationToken.None);

        Assert.Equal("notCollected", answer.Type);
    }

    [Fact]
    public async Task SupplyAsync_HostWithoutTheToolchainUnderItsRoot_IsNoToolchain()
    {
        var answer = await Builder(_ => Ended with { Output = "s3l-root /x\ns3l-supply none\n" }).SupplyAsync(Mini, new Supply(SupplyKind.AndroidPackages, ["platforms;android-37"]), CancellationToken.None);

        Assert.Equal("noToolchain", answer.Type);
    }

    [Fact]
    public async Task SupplyAsync_PackagesLeftOut_IsNotSupplied()
    {
        var answer = await Builder(_ => Ended with { Output = "s3l-root /x\ns3l-supply left platforms;android-37\n" }).SupplyAsync(Mini, new Supply(SupplyKind.AndroidPackages, ["platforms;android-37"]), CancellationToken.None);

        Assert.Equal("notSupplied", answer.Type);
    }

    [Fact]
    public async Task SurveyAsync_HostThatDoesNotAnswer_IsNotSurveyed()
    {
        var answer = await Builder(_ => Broken).SurveyAsync(Mini, true, CancellationToken.None);

        Assert.Equal("notSurveyed", answer.Type);
    }

    [Fact]
    public async Task ReadAsync_GitNotOnThisMachine_IsNotInstalled()
    {
        var answer = await new GitCopies(new ScriptedPrograms(ProgramRun.Missing)).ReadAsync("/Users/me/Sample", CancellationToken.None);

        Assert.Equal("notInstalled", answer.Type);
    }

    [Fact]
    public async Task DeliverAsync_MutagenNotOnThisMachine_IsNotInstalled()
    {
        var programs = new ScriptedPrograms(program => program == Settings.Value.Mutagen ? ProgramRun.Missing(program) : Ended);
        var shell = new RemoteShell(Settings);
        var copies = new FakeCopies();
        var courier = new MutagenCourier(programs, copies, new RsyncCourier(programs, copies, shell, Settings, TimeProvider.System), shell, Settings, TimeProvider.System, TestFolder.Create());

        var answer = await courier.DeliverAsync(Mini, Copy, false, CancellationToken.None);

        Assert.Equal("notInstalled", answer.Type);
    }
}
