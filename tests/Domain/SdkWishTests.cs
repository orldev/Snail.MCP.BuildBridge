namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class SdkWishTests
{
    /// <summary>A global.json with comments and other sections, as every Snail repository has: the sdk section is what counts.</summary>
    [Fact]
    public void In_AGlobalJsonWithCommentsAndOtherSections_IsItsSdkSection()
    {
        const string json = """
            {
              "$schema": "https://json.schemastore.org/global.json",
              "//": "why the band is pinned",
              "sdk": { "version": "10.0.302", "rollForward": "latestPatch", "allowPrerelease": false },
              "msbuild-sdks": { "Snail.Sdk": "0.4.2" }
            }
            """;

        Assert.Equal(new SdkWish("10.0.302", "latestPatch"), SdkWish.In(json));
    }

    [Theory]
    [InlineData("""{ "sdk": { "rollForward": "latestMajor" } }""")]
    [InlineData("""{ "sdk": { "version": "ten" } }""")]
    [InlineData("""{ "msbuild-sdks": { } }""")]
    [InlineData("not json")]
    public void In_AGlobalJsonNamingNoVersion_IsNull(string json) => Assert.Null(SdkWish.In(json));

    [Theory]
    [InlineData("10.0.302", "latestPatch", "10.0.302", true)]
    [InlineData("10.0.302", "latestPatch", "10.0.305", true)]
    [InlineData("10.0.302", "latestPatch", "10.0.301", false)]
    [InlineData("10.0.302", "latestPatch", "10.0.400", false)]
    [InlineData("10.0.302", "latestFeature", "10.0.400", true)]
    [InlineData("10.0.302", "latestMinor", "10.1.100", true)]
    [InlineData("10.0.302", "latestMinor", "11.0.100", false)]
    [InlineData("10.0.302", "latestMajor", "11.0.100", true)]
    [InlineData("10.0.302", "disable", "10.0.305", false)]
    [InlineData("10.0.302", "latestPatch", "", false)]
    public void IsMetBy_AHostsSdks_FollowsTheRollForwardRule(string wished, string rule, string held, bool expected) =>
        Assert.Equal(expected, new SdkWish(wished, rule).IsMetBy(held.Length > 0 ? [held] : []));

    [Theory]
    [InlineData("10.0.302", true)]
    [InlineData("10.0.100-rc.1.25451.107", true)]
    [InlineData("10.0", false)]
    [InlineData("10.0.302; rm -rf /", false)]
    public void IsVersion_AWord_IsTrueOnlyForAVersionAsDotnetSpellsOne(string word, bool expected) => Assert.Equal(expected, SdkWish.IsVersion(word));
}
