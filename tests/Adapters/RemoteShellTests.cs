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

    /// <summary>A connection made anew for every step is one more handshake a host's sshd may turn away: every command to a host goes over one, kept for the minutes the settings say.</summary>
    [Fact]
    public void Command_AnotherHost_SharesOneConnectionForTheMinutesTheSettingsSay()
    {
        var words = Shell.Command(new Host("mini", "dev@mini"), "true");

        Assert.Contains("ControlMaster=auto", words);
        Assert.Contains($"ControlPath={RemoteShell.SharedSocket}", words);
        Assert.Contains("ControlPersist=1800", words);
        Assert.Contains("BatchMode=yes", words);
    }

    /// <summary>A build holds its session for as long as it runs, and ten of them would hold every session the shared connection has: builds share connections of their own, a workspace always the same one.</summary>
    [Fact]
    public void LongCommand_ABuild_GoesOverALaneOfItsOwnAndAWorkspaceAlwaysTheSame()
    {
        var host = new Host("mini", "dev@mini");

        var words = Shell.LongCommand(host, "true", "workspaces/Sample/main");
        var again = Shell.LongCommand(host, "true", "workspaces/Sample/main");

        Assert.Equal(words, again);
        Assert.Contains("ControlMaster=auto", words);
        Assert.Contains(words, word => word.StartsWith($"ControlPath={RemoteShell.SharedSocket}-", StringComparison.Ordinal));
        Assert.DoesNotContain($"ControlPath={RemoteShell.SharedSocket}", words);
        Assert.InRange(RemoteShell.LaneOf("workspaces/Sample/main"), 0, RemoteShell.Lanes - 1);
    }

    [Fact]
    public void Command_SharingTurnedOff_OpensAConnectionOfItsOwn()
    {
        var shell = new RemoteShell(Options.Create(new BuildOptions { SshShareMinutes = 0 }));

        var words = shell.Command(new Host("mini", "dev@mini"), "true");

        Assert.DoesNotContain(words, word => word.StartsWith("Control", StringComparison.Ordinal));
    }

    /// <summary>rsync takes its remote shell as one line and splits it on spaces, so the line carries the same options and no word with a space in it.</summary>
    [Fact]
    public void SshLine_ForRsync_CarriesTheSameOptionsInOneLine()
    {
        Assert.Equal(string.Join(' ', Shell.Ssh), Shell.SshLine);
        Assert.DoesNotContain(Shell.Ssh, word => word.Contains(' ', StringComparison.Ordinal));
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
