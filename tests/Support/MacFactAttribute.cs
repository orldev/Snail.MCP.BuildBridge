namespace Snail.MCP.BuildBridge.Tests.Support;

/// <summary>A test that takes this machine as the host; skipped where it is not a Mac, since a host's scripts are written for macOS — <c>stat -f</c>, <c>df -g</c>, Rosetta.</summary>
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "This machine is the host in this test, and a host is a Mac.";
    }
}
