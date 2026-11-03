using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Configuration;
using Snail.MCP.BuildBridge.Tests.Support;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Chaos;

/// <summary>Deliveries that meet what a host made for itself, and deliveries that meet each other.</summary>
public sealed class CourierChaosTests
{
    private static readonly Programs Programs = new();

    private static readonly GitCopies Copies = new(Programs);

    private static RsyncCourier Courier()
    {
        var options = Options.Create(new BuildOptions { TransferSeconds = 60 });

        return new RsyncCourier(Programs, Copies, new RemoteShell(options), options, TimeProvider.System);
    }

    private static Host LocalHost() => new($"here-{Path.GetRandomFileName()[..6]}", "localhost", TestFolder.Create());

    private static async Task<LocalCopy> CopyAsync(string repository) => (await Copies.ReadAsync(repository, CancellationToken.None)).Value!;

    /// <summary>
    /// Breaks what a host built for itself. A build on the host writes into a folder git ignores — Anonym's
    /// AnonymCore.xcframework, a NativeAOT output, a generated project — that does not exist on this machine. The
    /// excludes come from what git ignores HERE, so the host's folder is not among them, and rsync's --delete takes it
    /// with the next delivery: every run that needs the previous run's output starts from nothing.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_OutputTheHostMadeInAnIgnoredFolderAbsentHere_IsKept()
    {
        var host = LocalHost();
        string repository = Repositories.Create();
        await File.AppendAllTextAsync(Path.Combine(repository, ".gitignore"), "out/\n");
        Repositories.Git(repository, "-c", "user.name=t", "-c", "user.email=t@e.com", "commit", "-qam", "ignore out");
        var copy = await CopyAsync(repository);
        var courier = Courier();
        await courier.DeliverAsync(host, copy, false, CancellationToken.None);
        string built = Path.Combine(host.Root, copy.Workspace.Folder, "out", "built.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(built)!);
        await File.WriteAllTextAsync(built, "made on the host", CancellationToken.None);

        var again = await courier.DeliverAsync(host, copy, false, CancellationToken.None);

        Assert.Null(again.Error);
        Assert.True(File.Exists(built), "the host's own output under an ignored folder was deleted by the next delivery");
    }

    /// <summary>The same break through a Mutagen session: the replica mode deletes what the copy lacks, unless the pattern says it is ignored.</summary>
    [MutagenFact]
    public async Task DeliverAsync_OutputTheHostMadeInAnIgnoredFolderAbsentHere_IsKeptByASessionToo()
    {
        var host = LocalHost();
        string repository = Repositories.Create();
        await File.AppendAllTextAsync(Path.Combine(repository, ".gitignore"), "out/\n");
        Repositories.Git(repository, "-c", "user.name=t", "-c", "user.email=t@e.com", "commit", "-qam", "ignore out");
        var copy = await CopyAsync(repository);
        var options = Options.Create(new BuildOptions { Sync = Delivery.Mutagen, TransferSeconds = 60 });
        var shell = new RemoteShell(options);
        var courier = new MutagenCourier(Programs, Copies, new RsyncCourier(Programs, Copies, shell, options, TimeProvider.System), shell, options, TimeProvider.System);

        try
        {
            await courier.DeliverAsync(host, copy, false, CancellationToken.None);
            string built = Path.Combine(host.Root, copy.Workspace.Folder, "out", "built.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(built)!);
            await File.WriteAllTextAsync(built, "made on the host", CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(repository, "later.txt"), "later", CancellationToken.None);

            var again = await courier.DeliverAsync(host, copy, false, CancellationToken.None);

            Assert.Null(again.Error);
            Assert.True(File.Exists(Path.Combine(host.Root, copy.Workspace.Folder, "later.txt")));
            Assert.True(File.Exists(built), "the host's own output under an ignored folder was deleted by the session");
        }
        finally
        {
            await courier.ForgetAsync(host, copy.Workspace, CancellationToken.None);
        }
    }

    /// <summary>Two deliveries of one copy at once into one workspace: both end, and the workspace is whole afterwards.</summary>
    [Fact]
    public async Task DeliverAsync_TwoAtOnceIntoOneWorkspace_BothEndAndTheCopyIsWhole()
    {
        var host = LocalHost();
        var copy = await CopyAsync(Repositories.Create());
        var courier = Courier();

        var both = await Task.WhenAll(courier.DeliverAsync(host, copy, false, CancellationToken.None), courier.DeliverAsync(host, copy, false, CancellationToken.None));

        Assert.All(both, delivery => Assert.Null(delivery.Error));
        Assert.True(File.Exists(Path.Combine(host.Root, copy.Workspace.Folder, "src", "Program.cs")));
    }

    /// <summary>A copy whose root was deleted between the read and the delivery: an answer, not an exception.</summary>
    [Fact]
    public async Task DeliverAsync_CopyGoneBeforeTheDelivery_AnswersWithAnError()
    {
        var host = LocalHost();
        string repository = Repositories.Create();
        var copy = await CopyAsync(repository);
        Directory.Delete(repository, true);

        var answer = await Courier().DeliverAsync(host, copy, false, CancellationToken.None);

        Assert.NotNull(answer.Error);
    }
}
