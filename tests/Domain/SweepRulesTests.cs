namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class SweepRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsStale_WorkspaceBuiltWithinTheAge_IsKept()
    {
        var rules = new SweepRules(WorkspaceDays: 14);

        Assert.False(rules.IsStale(Now.AddDays(-13), Now));
    }

    [Fact]
    public void IsStale_WorkspaceBuiltPastTheAge_IsDoneWith()
    {
        var rules = new SweepRules(WorkspaceDays: 14);

        Assert.True(rules.IsStale(Now.AddDays(-15), Now));
    }

    [Fact]
    public void IsLow_RoomUnderTheFloor_AsksForASweepFirst()
    {
        var rules = new SweepRules(LowDiskGigabytes: 20);

        Assert.True(rules.IsLow(19.5));
        Assert.False(rules.IsLow(20));
    }
}
