using System.Globalization;
using System.Text.RegularExpressions;

namespace Snail.MCP.BuildBridge.Domain;

/// <summary>What a test run said at its end, summed over every test project it ran.</summary>
public sealed partial record TestSummary(int Passed, int Failed, int Skipped, int Total)
{
    /// <summary>Whether every test that ran passed.</summary>
    public bool IsGreen => Failed == 0 && Total > 0;

    /// <summary>The summary a log carries, in the words of VSTest or of the testing platform; null when the log has none.</summary>
    /// <remarks>
    /// VSTest ends each project with <c>Passed! - Failed: 0, Passed: 61, Skipped: 1, Total: 62</c>; Microsoft.Testing.Platform
    /// with <c>total: 62 failed: 0 succeeded: 61 skipped: 1</c>. A log of several projects carries several lines, and
    /// their numbers are added. The lines are English because every run exports DOTNET_CLI_UI_LANGUAGE=en.
    /// </remarks>
    public static TestSummary? Parse(string log)
    {
        var vstest = VsTest().Matches(log).Select(match => new TestSummary(Number(match, 2), Number(match, 1), Number(match, 3), Number(match, 4)));
        var platform = Platform().Matches(log).Select(match => new TestSummary(Number(match, 3), Number(match, 2), Number(match, 4), Number(match, 1)));
        var summaries = vstest.Concat(platform).ToList();

        if (summaries.Count == 0)
            return null;

        return new TestSummary(summaries.Sum(summary => summary.Passed), summaries.Sum(summary => summary.Failed), summaries.Sum(summary => summary.Skipped), summaries.Sum(summary => summary.Total));
    }

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?:Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)")]
    private static partial Regex VsTest();

    [GeneratedRegex(@"total:\s+(\d+)\s+failed:\s+(\d+)\s+succeeded:\s+(\d+)\s+skipped:\s+(\d+)")]
    private static partial Regex Platform();
}

/// <summary>A build as it ran on a host: where, what was delivered, how the command ended, and what its tests said.</summary>
/// <param name="Host">The host it ran on.</param>
/// <param name="Kind">The toolchain it drove.</param>
/// <param name="Command">The command as it ran in the workspace.</param>
/// <param name="Delivery">The delivery that went before it.</param>
/// <param name="Execution">How the command ended, and where its log is.</param>
/// <param name="Tests">What the tests said, when the log carries a summary.</param>
/// <param name="Swept">What the sweeps around the run gave back.</param>
public sealed record BuildReport(Host Host, BuildKind Kind, IReadOnlyList<string> Command, Delivery Delivery, Execution Execution, TestSummary? Tests, IReadOnlyList<SweepResult> Swept)
{
    /// <summary>Whether the command ended as it meant to and no test failed.</summary>
    public bool IsSuccess => Execution.IsSuccess && Tests is not { Failed: > 0 };
}
