using System.Text.RegularExpressions;

namespace Snail.MCP.BuildBridge.Domain;

/// <summary>The Android SDK packages a build asks for beyond what provisioning put under the root, as sdkmanager names them, and the one a failed .NET Android build names itself.</summary>
/// <remarks>
/// What a build needs comes from the build, not from the settings: a call names the packages, and a build that failed
/// for want of a platform — XA5207 says which API level — names it in its log. The settings only seed a new host.
/// </remarks>
public static partial class AndroidPackages
{
    /// <summary>The turn a supply takes on a host, so two runs do not drive sdkmanager into one folder at once.</summary>
    public const string Turn = "android/packages";

    /// <summary>Whether a word is a package as sdkmanager names one — <c>platforms;android-37</c>, <c>system-images;android-36;google_apis;arm64-v8a</c> — and nothing a shell would read.</summary>
    public static bool IsName(string package) => Name().IsMatch(package);

    /// <summary>The platform a .NET Android build said it lacked, as a package; null when the log names none.</summary>
    public static string? MissingPlatformIn(string log)
    {
        var lacking = Lacking().Match(log);

        return lacking.Success ? $"platforms;android-{lacking.Groups[1].Value}" : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*(?:;[A-Za-z0-9][A-Za-z0-9._-]*)*$")]
    private static partial Regex Name();

    [GeneratedRegex(@"XA5207:.*?API level (\d+)")]
    private static partial Regex Lacking();
}
