namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class TestSummaryTests
{
    [Fact]
    public void Parse_VsTestLinesOfTwoProjects_AddsThemUp()
    {
        const string log = """
            Starting test execution, please wait...
            Passed!  - Failed:     0, Passed:    61, Skipped:     1, Total:    62, Duration: 5 s - A.Tests.dll (net10.0)
            Failed!  - Failed:     2, Passed:    10, Skipped:     0, Total:    12, Duration: 1 s - B.Tests.dll (net10.0)
            """;

        var summary = TestSummary.Parse(log);

        Assert.Equal(new TestSummary(71, 2, 1, 74), summary);
        Assert.False(summary!.IsGreen);
    }

    [Fact]
    public void Parse_TestingPlatformLine_IsRead()
    {
        const string log = "Test run summary: Passed! - total: 8 failed: 0 succeeded: 7 skipped: 1 duration: 1.2s";

        Assert.Equal(new TestSummary(7, 0, 1, 8), TestSummary.Parse(log));
    }

    [Fact]
    public void Parse_LogWithoutTests_IsNull()
    {
        Assert.Null(TestSummary.Parse("Build succeeded.\n    0 Warning(s)\n"));
    }
}
