using System.Text;
using BenchmarkDotNet.Attributes;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Benchmarks;

/// <summary>The hot paths of the server in process: what it parses out of rsync, a test run, a survey, and what it decides from one; the perf workflow compares their allocations with the baseline.</summary>
/// <remarks>Every run of a build goes through these: ten thousand itemized lines are one delivery of a mid-sized repository, five megabytes one test log, two hundred workspaces a host after a month.</remarks>
[MemoryDiagnoser]
public class ParsingBenchmarks
{
    private static readonly string Itemized = ItemizedLines(10_000);

    private static readonly string Log = TestLog(5 * 1024 * 1024);

    private static readonly string SurveyOutput = SurveyLines(200, 50);

    private static readonly Survey Surveyed = Surveys.Of(new Host("mini", "dev@mini"), SurveyOutput);

    private static readonly SweepRules Rules = new();

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The files of a delivery of ten thousand changed files, read from rsync's itemized output.</summary>
    [Benchmark]
    public IReadOnlyList<string> ChangedFiles() => Rsync.Changed(Itemized);

    /// <summary>What the tests said, read from a log of five megabytes with twenty summaries in it.</summary>
    [Benchmark]
    public TestSummary? TestsSaid() => TestSummary.Parse(Log);

    /// <summary>A survey of two hundred workspaces and fifty images, read from the script's lines.</summary>
    [Benchmark]
    public Survey SurveyRead() => Surveys.Of(new Host("mini", "dev@mini"), SurveyOutput);

    /// <summary>What a caches sweep decides from that survey.</summary>
    [Benchmark]
    public IReadOnlyList<SweepItem> SweepDecided() => Sweep.Decide(Surveyed, Rules, Now, SweepScope.Caches);

    private static string ItemizedLines(int count)
    {
        var text = new StringBuilder();
        for (int number = 0; number < count; number++)
            text.Append(number % 10 == 0 ? "*deleting   " : ">f+++++++++ ").Append("src/Area").Append(number % 37).Append("/File").Append(number).AppendLine(".cs");
        text.AppendLine("Total transferred file size: 1,234,567 B");

        return text.ToString();
    }

    private static string TestLog(int bytes)
    {
        var text = new StringBuilder(bytes + 4096);
        const string line = "  Passed Snail.Sample.Tests.SomethingTests.Subject_Condition_ExpectedResult [12 ms]";
        int project = 0;
        while (text.Length < bytes)
        {
            for (int number = 0; number < 2000; number++)
                text.AppendLine(line);
            text.AppendLine($"Passed!  - Failed:     0, Passed:   {2000 + project}, Skipped:     1, Total:  {2001 + project}, Duration: 2 s - Sample{project++}.Tests.dll (net10.0)");
        }

        return text.ToString();
    }

    private static string SurveyLines(int workspaces, int images)
    {
        var text = new StringBuilder();
        for (int number = 0; number < workspaces; number++)
            text.AppendLine($"s3l-ws Repo{number % 20} branch-{number} {1761000000 + number * 3600} {1761000000 + number * 7200} {1024 * (number + 1)}");
        for (int number = 0; number < workspaces / 2; number++)
            text.AppendLine($"s3l-derived Repo{number % 20}-branch-{number}");
        for (int number = 0; number < images; number++)
            text.AppendLine($"s3l-image {number:x12} 2026-10-{1 + number % 28:00} 19:52:13 +0300 MSK {number + 1}.5GB");
        text.AppendLine("s3l-buildcache 12.3GB");
        text.AppendLine("s3l-packages 2345678");
        text.AppendLine("s3l-free 612");

        return text.ToString();
    }
}
