namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class WorkspaceKeyTests
{
    [Fact]
    public void Folder_BranchWithSlashes_LiesUnderTheRepositoryAsOneFolder()
    {
        var key = new WorkspaceKey("Anonym", "feat/web-3.0");

        Assert.Equal("workspaces/Anonym/feat-web-3.0", key.Folder);
    }

    [Theory]
    [InlineData("main", "main")]
    [InlineData("release/2.0", "release-2.0")]
    [InlineData("fix/a b", "fix-a-b")]
    [InlineData("héllo", "héllo")]
    public void Safe_AnyBranch_KeepsLettersDigitsDotsAndDashes(string branch, string expected) =>
        Assert.Equal(expected, WorkspaceKey.Safe(branch));

    [Fact]
    public void Workspace_OfACopy_IsNamedByRepositoryAndBranch()
    {
        var copy = new LocalCopy("/Users/me/Anonym", "Anonym", "main", "abc123", false);

        Assert.Equal(new WorkspaceKey("Anonym", "main"), copy.Workspace);
    }
}
