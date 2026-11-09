using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Tests.Support;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

public sealed class GitCopiesTests
{
    private static readonly GitCopies Copies = new(new Programs());

    [Fact]
    public async Task ReadAsync_PathInsideACopy_NamesItsRootRepositoryBranchAndCommit()
    {
        string repository = Repositories.Create("Anonym");

        var answer = await Copies.ReadAsync(Path.Combine(repository, "src"), CancellationToken.None);

        var copy = Assert.IsType<LocalCopy>(answer.Value);
        Assert.Equal(Repositories.Git(repository, "rev-parse", "--show-toplevel"), copy.Path);
        Assert.Equal("Anonym", copy.Repository);
        Assert.Equal("main", copy.Branch);
        Assert.Equal(Repositories.Git(repository, "rev-parse", "HEAD"), copy.Commit);
        Assert.False(copy.IsWorktree);
    }

    [Fact]
    public async Task ReadAsync_DetachedHead_NamesTheBranchByTheCommit()
    {
        string repository = Repositories.Create();
        Repositories.Git(repository, "checkout", "-q", "--detach");

        var answer = await Copies.ReadAsync(repository, CancellationToken.None);

        Assert.Equal(answer.Value!.Commit![..7], answer.Value.Branch);
    }

    [Fact]
    public async Task ReadAsync_LinkedWorktree_IsToldFromTheMainCopy()
    {
        string repository = Repositories.Create();
        string worktree = Path.Combine(TestFolder.Create(), "side");
        Repositories.Git(repository, "worktree", "add", "-q", "-b", "side", worktree);

        var answer = await Copies.ReadAsync(worktree, CancellationToken.None);

        Assert.True(answer.Value!.IsWorktree);
        Assert.Equal("side", answer.Value.Branch);
    }

    [Fact]
    public async Task ReadAsync_PathOutsideAnyCopy_SaysSo()
    {
        var answer = await Copies.ReadAsync(TestFolder.Create(), CancellationToken.None);

        Assert.Null(answer.Value);
        Assert.Contains("not inside a git working copy", answer.Error, StringComparison.Ordinal);
        Assert.Equal("notACopy", answer.Type);
    }

    [Fact]
    public async Task IgnoredAsync_IgnoredFolders_AreListedAsOneEntryWithASlash()
    {
        string repository = Repositories.Create();
        var copy = (await Copies.ReadAsync(repository, CancellationToken.None)).Value!;

        var ignored = await Copies.IgnoredAsync(copy, CancellationToken.None);

        Assert.Contains("src/bin/", ignored);
        Assert.Contains("secrets/", ignored);
        Assert.DoesNotContain("notes.txt", ignored);
    }

    [Theory]
    [InlineData("", "bin/", "/**/bin/")]
    [InlineData("", "/artifacts", "/artifacts")]
    [InlineData("apple", "Anonym.xcodeproj/", "/apple/**/Anonym.xcodeproj/")]
    [InlineData("apple", "Packages/AnonymKit/Frameworks/", "/apple/Packages/AnonymKit/Frameworks/")]
    [InlineData("", "!keep.txt", "!/**/keep.txt")]
    [InlineData("", "*.log", "/**/*.log")]
    public void Translated_AGitignoreLine_IsAMutagenPatternAnchoredAtItsFolder(string folder, string line, string expected) =>
        Assert.Equal(expected, GitCopies.Translated(folder, line));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# a comment")]
    public void Translated_ABlankOrAComment_IsNothing(string line) => Assert.Null(GitCopies.Translated("", line));

    [Fact]
    public async Task IgnorePatternsAsync_ACopyWithANestedGitignore_AnchorsEachAtItsFolder()
    {
        string repository = Repositories.Create();
        Directory.CreateDirectory(Path.Combine(repository, "apple"));
        await File.WriteAllTextAsync(Path.Combine(repository, "apple", ".gitignore"), "Generated.xcodeproj/\n", CancellationToken.None);
        var copy = (await Copies.ReadAsync(repository, CancellationToken.None)).Value!;

        var patterns = await Copies.IgnorePatternsAsync(copy, CancellationToken.None);

        Assert.Contains("/**/bin/", patterns);
        Assert.Contains("/**/secrets/", patterns);
        Assert.Contains("/apple/**/Generated.xcodeproj/", patterns);
    }
}
