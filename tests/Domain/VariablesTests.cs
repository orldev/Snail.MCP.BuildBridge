namespace Snail.MCP.BuildBridge.Tests.Domain;

public sealed class VariablesTests
{
    [Theory]
    [InlineData("TOKEN", true)]
    [InlineData("_private", true)]
    [InlineData("Aspnet_Core9", true)]
    [InlineData("1TOKEN", false)]
    [InlineData("A-B", false)]
    [InlineData("", false)]
    [InlineData("A B", false)]
    public void IsName_AWord_IsTrueForLettersDigitsAndUnderscoresNotStartingWithADigit(string word, bool expected) =>
        Assert.Equal(expected, Variables.IsName(word));

    [Fact]
    public void Parsed_Assignments_KeepTheValueWholeAndTheLastOfANameGivenTwice()
    {
        var (variables, odd) = Variables.Parsed(["A=1", "URL=https://x/y?z=1=2", "A=2"]);

        Assert.Null(odd);
        Assert.Equal("2", variables["A"]);
        Assert.Equal("https://x/y?z=1=2", variables["URL"]);
    }

    [Fact]
    public void Parsed_AWordWithoutAnEqualsSign_IsTheOddOne() => Assert.Equal("TOKEN", Variables.Parsed(["A=1", "TOKEN"]).Odd);
}
