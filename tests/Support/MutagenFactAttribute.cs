using System.Diagnostics;

namespace Snail.MCP.BuildBridge.Tests.Support;

/// <summary>A test that needs mutagen on this machine; skipped where it is not installed.</summary>
public sealed class MutagenFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> IsInstalled = new(HasMutagen);

    public MutagenFactAttribute()
    {
        if (!IsInstalled.Value)
            Skip = "mutagen is not installed on this machine: brew install mutagen-io/mutagen/mutagen.";
    }

    private static bool HasMutagen()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("mutagen", "version") { RedirectStandardOutput = true, RedirectStandardError = true });
            process!.WaitForExit(10_000);

            return process.ExitCode == 0;
        }
        catch (Exception absent) when (absent is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
