namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class CommandsTests
{
    private static readonly WorkspaceKey Key = new("Anonym", "feat/web-3.0");

    [Fact]
    public void XcodeShaped_NoDerivedData_PutsItUnderTheRoot()
    {
        var shaped = XcodeCommand.Shaped(["build", "-scheme", "App"], Key);

        Assert.Equal(["build", "-scheme", "App", "-derivedDataPath", "../../../../derived/Anonym-feat-web-3.0"], shaped);
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
    public void SimulatorsNamed_ByDestination_IsTheNameOfASimulatorOnly(string destination, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], XcodeCommand.SimulatorsNamed(["test", "-destination", destination]));

    [Fact]
    public void SimulatorsNamed_NoDestination_IsNone() => Assert.Empty(XcodeCommand.SimulatorsNamed(["build"]));

    /// <summary>Two destinations by name are two simulators to clone, in their order; one by id stays as it is between them.</summary>
    [Fact]
    public void SimulatorsNamed_SeveralDestinations_AreEachInOrder()
    {
        string[] arguments = ["test", "-destination", "platform=iOS Simulator,name=iPhone 17 Pro", "-destination", "platform=iOS Simulator,id=ABCD", "-destination", "platform=iOS Simulator,name=iPad 11-inch (A16),OS=27.1"];

        Assert.Equal(["iPhone 17 Pro", "iPad 11-inch (A16)"], XcodeCommand.SimulatorsNamed(arguments));
    }

    [Fact]
    public void OnDevices_DestinationsByName_PointEachAtItsIdAndDropNameAndOs()
    {
        string[] arguments = ["test", "-destination", "platform=iOS Simulator,name=iPhone 17,OS=27.1", "-destination", "platform=iOS Simulator,id=ABCD", "-destination", "platform=iOS Simulator,name=iPad 11-inch (A16)", "-scheme", "App"];

        var pointed = XcodeCommand.OnDevices(arguments, ["UDID-9", "UDID-10"]);

        Assert.Equal(["test", "-destination", "platform=iOS Simulator,id=UDID-9", "-destination", "platform=iOS Simulator,id=ABCD", "-destination", "platform=iOS Simulator,id=UDID-10", "-scheme", "App"], pointed);
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

/// <summary>Where a spec would be for the project xcodebuild is pointed at, and the command that generates it.</summary>
public sealed class XcodeGenerationTests
{
    [Fact]
    public void ProjectFolderOf_ProjectInAFolder_IsThatFolder() =>
        Assert.Equal("apple", XcodeCommand.ProjectFolderOf(["build", "-project", "apple/Anonym.xcodeproj", "-scheme", "App"]));

    [Fact]
    public void ProjectFolderOf_NoProjectNamed_IsTheRoot() =>
        Assert.Equal(".", XcodeCommand.ProjectFolderOf(["build", "-scheme", "App"]));

    [Fact]
    public void Generation_AFolder_NamesItsSpecAndProject() =>
        Assert.Equal(["xcodegen", "generate", "--spec", "apple/project.yml", "--project", "apple"], XcodeCommand.Generation("apple"));

    [Fact]
    public void Generation_TheRoot_NamesTheSpecAlone() =>
        Assert.Equal(["xcodegen", "generate", "--spec", "project.yml", "--project", "."], XcodeCommand.Generation("."));
}

/// <summary>A dotnet command wrapped in docker run of the kind's image, with the workspace and the packages where dotnet inside finds them.</summary>
public sealed class ContainerCommandTests
{
    private static readonly WorkspaceKey Key = new("Sample", "main");

    [Fact]
    public void Shaped_LinuxTest_RunsDotnetInTheDotnetImageWithTheWorkspaceMounted()
    {
        var shaped = ContainerCommand.Shaped(BuildKind.Linux, ["test", "-c", "Release"], Key, "/Users/dev/.s3l-build", "10.0.302");

        Assert.Equal("run", shaped[0]);
        Assert.Contains("snail.owner=buildbridge/Sample/main", shaped);
        Assert.Contains("/Users/dev/.s3l-build/workspaces/Sample/main/Sample:/work", shaped);
        Assert.Contains("/Users/dev/.s3l-build/nuget:/root/.nuget/packages", shaped);
        Assert.Contains("s3l-build-dotnet:10.0.302", shaped);
        Assert.Equal(["dotnet", "test", "-c", "Release"], shaped.TakeLast(4));
        Assert.DoesNotContain("--platform", shaped);
    }

    [Fact]
    public void Shaped_Android_RunsTheAndroidImageAsAmd64()
    {
        var shaped = ContainerCommand.Shaped(BuildKind.Android, ["build"], Key, "/Users/dev/.s3l-build", "10.0.302");

        Assert.Contains("s3l-build-android:10.0.302", shaped);
        Assert.Equal("linux/amd64", shaped[shaped.ToList().IndexOf("--platform") + 1]);
    }

    [Fact]
    public void Shaped_NoSdkVersion_RunsTheLatestTag()
    {
        var shaped = ContainerCommand.Shaped(BuildKind.Linux, ["build"], Key, "/root", string.Empty);

        Assert.Contains("s3l-build-dotnet:latest", shaped);
    }

    [Fact]
    public void Shaped_AKindOfTheHost_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ContainerCommand.Shaped(BuildKind.Dotnet, ["build"], Key, "/root", "10.0.302"));
}

/// <summary>A script of the repository as a run starts it.</summary>
public sealed class ScriptCommandTests
{
    [Fact]
    public void Shaped_APathWithAFolder_IsRunAsItIsWithItsArguments() =>
        Assert.Equal(["tools/native/build-xcframework.sh", "Debug"], ScriptCommand.Shaped(["tools/native/build-xcframework.sh", "Debug"]));

    [Fact]
    public void Shaped_ABareName_IsRunFromTheWorkspaceRoot() =>
        Assert.Equal(["./build.sh"], ScriptCommand.Shaped(["build.sh"]));

    [Theory]
    [InlineData("tools/build.sh", true)]
    [InlineData("build.sh", true)]
    [InlineData("/usr/bin/true", false)]
    [InlineData("../other/build.sh", false)]
    [InlineData("", false)]
    public void IsInside_APath_SaysWhetherItStaysInTheWorkspace(string path, bool expected) =>
        Assert.Equal(expected, ScriptCommand.IsInside(path));
}
