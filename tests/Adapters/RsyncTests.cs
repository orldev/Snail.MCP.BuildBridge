using Snail.MCP.BuildBridge.Adapters;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

public sealed class RsyncTests
{
    /// <summary>rsync 3 writes eleven flag characters, openrsync nine; the path follows the first space either way.</summary>
    [Fact]
    public void Changed_ItemsOfRsyncAndOpenrsync_AreTheFilesWrittenAndDeleted()
    {
        const string output = """
            >f+++++++++ src/Program.cs
            >f.st...... README.md
            cd+++++++++ src/
            .d..t...... docs/
            <f+++++++ tests/A Test.cs
            cL+++++++ link
            *deleting   notes.txt
            *deleting   old/
            
            Number of files: 12
            Total transferred file size: 1,234,567 B
            """;

        var files = Rsync.Changed(output);

        Assert.Equal(["src/Program.cs", "README.md", "tests/A Test.cs", "link", "notes.txt"], files);
    }

    [Fact]
    public void Changed_NoOutput_IsNothing() => Assert.Empty(Rsync.Changed(string.Empty));

    [Fact]
    public void TransferredBytes_TheStatsLine_IsItsNumberWithoutTheCommas() =>
        Assert.Equal(1_234_567, Rsync.TransferredBytes("Number of files: 12\nTotal transferred file size: 1,234,567 B\nTotal time: 0.1 s\n"));

    [Fact]
    public void TransferredBytes_NoStatsLine_IsZero() => Assert.Equal(0, Rsync.TransferredBytes(">f+++++++++ a.txt\n"));
}
