using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Adapters;
using Snail.MCP.BuildBridge.Configuration;

namespace Snail.MCP.BuildBridge.Tests.Adapters;

/// <summary>The command a script becomes: sh on this machine, and sh on a host whatever its login shell is.</summary>
public sealed class RemoteShellTests
{
    private static readonly RemoteShell Shell = new(Options.Create(new BuildOptions()));

    [Fact]
    public void Command_ThisMachine_RunsTheScriptInSh()
    {
        var words = Shell.Command(new Host("here", "localhost"), "echo one");

        Assert.Equal(["/bin/sh", "-c", "echo one"], words);
    }

    [Fact]
    public void Command_AnotherHost_HandsTheScriptToShThroughTheLoginShell()
    {
        var words = Shell.Command(new Host("mini", "dev@mini"), "for w in $WORDS; do echo \"$w\"; done");

        Assert.Equal("ssh", words[0]);
        Assert.Equal("dev@mini", words[^2]);
        Assert.Equal("exec /bin/sh -c 'for w in $WORDS; do echo \"$w\"; done'", words[^1]);
    }

    [Fact]
    public void Command_AnotherHost_KeepsASingleQuoteOfTheScript()
    {
        var words = Shell.Command(new Host("mini", "dev@mini"), "echo 'it''s'");

        Assert.Equal("exec /bin/sh -c 'echo '\\''it'\\'''\\''s'\\'''", words[^1]);
    }

    [Fact]
    public void Prelude_HomeRoot_SpellsTheRootThroughHome()
    {
        var prelude = RemoteShell.Prelude(new Host("mini", "dev@mini"));

        Assert.Contains("export PATH=\"$HOME/.s3l-build\"/bin:\"$HOME/.s3l-build\"/dotnet:/usr/local/bin:/opt/homebrew/bin:\"$HOME/.orbstack/bin\":\"$PATH\"", prelude, StringComparison.Ordinal);
    }

    /// <summary>The toolchain under the root is named the way gradle and dotnet look for it, only where it is.</summary>
    [Fact]
    public void Prelude_AnyHost_NamesTheAndroidToolchainUnderTheRootWhereItIs()
    {
        var prelude = RemoteShell.Prelude(new Host("mini", "dev@mini"));

        Assert.Contains("[ -x \"$HOME/.s3l-build/jdk/current/bin/java\" ] && export JAVA_HOME=\"$HOME/.s3l-build/jdk/current\"", prelude, StringComparison.Ordinal);
        Assert.Contains("export ANDROID_HOME=\"$HOME/.s3l-build/android\"", prelude, StringComparison.Ordinal);
        Assert.Contains("GRADLE_USER_HOME=\"$HOME/.s3l-build/gradle\"", prelude, StringComparison.Ordinal);
        Assert.Contains("export ANDROID_NDK_HOME=\"$HOME/.s3l-build/android/ndk\"/\"$S3L_NDK\"", prelude, StringComparison.Ordinal);
        Assert.Contains("npm_config_cache=\"$HOME/.s3l-build/npm\" PLAYWRIGHT_BROWSERS_PATH=\"$HOME/.s3l-build/playwright\"", prelude, StringComparison.Ordinal);
    }
}
