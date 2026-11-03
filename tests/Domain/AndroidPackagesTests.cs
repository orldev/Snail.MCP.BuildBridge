namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class AndroidPackagesTests
{
    [Theory]
    [InlineData("platforms;android-37", true)]
    [InlineData("build-tools;36.0.0", true)]
    [InlineData("system-images;android-36;google_apis;arm64-v8a", true)]
    [InlineData("ndk;28.2.13676358", true)]
    [InlineData("platforms;android-37; rm -rf /", false)]
    [InlineData("$(id)", false)]
    [InlineData("", false)]
    [InlineData(";platforms", false)]
    public void IsName_AWord_IsTrueOnlyForWhatSdkmanagerTakes(string word, bool expected) =>
        Assert.Equal(expected, AndroidPackages.IsName(word));

    /// <summary>XA5207 names the API level a .NET Android build could not find android.jar for; that platform is what the build lacks.</summary>
    [Fact]
    public void MissingPlatformIn_ALogWithXA5207_IsThatPlatform()
    {
        const string log = """
            Determining projects to restore...
            /Users/dev/.nuget/packages/microsoft.android.sdk.darwin/36.0.0/tools/Xamarin.Android.Common.targets(123,5): error XA5207: Could not find android.jar for API level 37. This means the Android SDK platform for API level 37 is not installed; it's available via dotnet build -t:InstallAndroidDependencies.
            Build FAILED.
            """;

        Assert.Equal("platforms;android-37", AndroidPackages.MissingPlatformIn(log));
    }

    [Fact]
    public void MissingPlatformIn_ALogWithoutIt_IsNull() => Assert.Null(AndroidPackages.MissingPlatformIn("error CS1002: ; expected\nBuild FAILED.\n"));
}
