namespace Snail.MCP.BuildBridge.Tests.Support;

/// <summary>A test that clones a simulator on this Mac, skipped — not passed — unless asked for.</summary>
/// <remarks>Run with <c>SNAIL_BUILDBRIDGE_SIMULATOR=1 dotnet test</c>; it makes one device it deletes again, named so that nothing of the developer's own can be mistaken for it.</remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SimulatorFactAttribute : FactAttribute
{
    public const string Variable = "SNAIL_BUILDBRIDGE_SIMULATOR";

    public SimulatorFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"clones a simulator on this Mac: set {Variable}=1 to run it";
    }
}
