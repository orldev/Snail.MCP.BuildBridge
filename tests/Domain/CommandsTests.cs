namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class CommandsTests
{
    private static readonly WorkspaceKey Key = new("Anonym", "feat/web-3.0");

    [Fact]
    public void XcodeShaped_NoDerivedData_PutsItUnderTheRoot()
    {
        var shaped = XcodeCommand.Shaped(["build", "-scheme", "App"], Key);

        Assert.Equal(["build", "-scheme", "App", "-derivedDataPath", "../../../derived/Anonym-feat-web-3.0"], shaped);
    }

    [Fact]
    public void XcodeShaped_DerivedDataGiven_IsLeftAlone()
    {
        string[] given = ["build", "-derivedDataPath", "/tmp/dd"];

        Assert.Equal(given, XcodeCommand.Shaped(given, Key));
    }

    [Theory]
    [InlineData("platform=iOS Simulator,name=iPhone 17,OS=27.1", "iPhone 17")]
    [InlineData("platform=iOS Simulator,id=ABCD", null)]
    [InlineData("platform=macOS", null)]
    [InlineData("generic/platform=iOS", null)]
    public void SimulatorNamed_ByDestination_IsTheNameOfASimulatorOnly(string destination, string? expected) =>
        Assert.Equal(expected, XcodeCommand.SimulatorNamed(["test", "-destination", destination]));

    [Fact]
    public void SimulatorNamed_NoDestination_IsNull() => Assert.Null(XcodeCommand.SimulatorNamed(["build"]));

    [Fact]
    public void OnDevice_DestinationByName_PointsAtTheIdAndDropsNameAndOs()
    {
        var pointed = XcodeCommand.OnDevice(["test", "-destination", "platform=iOS Simulator,name=iPhone 17,OS=27.1", "-scheme", "App"], "UDID-9");

        Assert.Equal(["test", "-destination", "platform=iOS Simulator,id=UDID-9", "-scheme", "App"], pointed);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("run")]
    public void DockerShaped_BuildOrRun_CarriesTheOwnerLabelFirst(string verb)
    {
        var shaped = DockerCommand.Shaped([verb, "-t", "x", "."], Key);

        Assert.Equal([verb, "--label", "snail.owner=buildbridge/Anonym/feat-web-3.0", "-t", "x", "."], shaped);
    }

    [Fact]
    public void DockerShaped_ComposeWithoutProject_IsTheWorkspacesProject()
    {
        var shaped = DockerCommand.Shaped(["compose", "up", "-d"], Key);

        Assert.Equal(["compose", "-p", "s3l-anonym-feat-web-3.0", "up", "-d"], shaped);
    }

    [Fact]
    public void DockerShaped_ComposeWithProject_IsLeftAlone()
    {
        string[] given = ["compose", "-p", "mine", "up"];

        Assert.Equal(given, DockerCommand.Shaped(given, Key));
    }

    [Fact]
    public void DockerShaped_OtherVerb_IsLeftAlone()
    {
        string[] given = ["images"];

        Assert.Equal(given, DockerCommand.Shaped(given, Key));
    }
}
