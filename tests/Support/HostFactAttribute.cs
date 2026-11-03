namespace Snail.MCP.BuildBridge.Tests.Support;

/// <summary>A test that works a real host over ssh, skipped — not passed — unless one is named.</summary>
/// <remarks>
/// Run with <c>SNAIL_BUILDBRIDGE_TEST_HOST=user@host dotnet test</c>. The test delivers a copy of its own making into a
/// workspace it removes again, under the host's default root.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class HostFactAttribute : FactAttribute
{
    public const string Variable = "SNAIL_BUILDBRIDGE_TEST_HOST";

    public HostFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"works a real host over ssh: set {Variable}=user@host to run it";
    }

    /// <summary>The address the variable names.</summary>
    public static string Address => Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
}
