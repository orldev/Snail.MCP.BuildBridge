using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snail.MCP.BuildBridge.Domain;

/// <summary>The .NET SDK a copy's global.json asks for: a version, and how far from it dotnet may roll forward.</summary>
/// <param name="Version">The version named.</param>
/// <param name="RollForward">The roll-forward rule, as global.json spells it; latestPatch when it says nothing.</param>
/// <remarks>
/// The SDK a build needs is the copy's to say, not the settings': a host holds the versions the copies on it name,
/// and a run that finds none meeting its copy's wish puts that version under the root first. The rule is read the
/// way dotnet reads it — a patch within the feature band, a feature within the minor, a minor within the major, or
/// any — so a host holding a newer patch is not asked for the older one.
/// </remarks>
public sealed partial record SdkWish(string Version, string RollForward = "latestPatch")
{
    /// <summary>The file a copy states its wish in.</summary>
    public const string FileName = "global.json";

    /// <summary>The wish <paramref name="globalJson"/> states; null when it names no SDK version, or one that is not a version.</summary>
    public static SdkWish? In(string globalJson)
    {
        try
        {
            using var document = JsonDocument.Parse(globalJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("sdk", out var sdk) || sdk.ValueKind != JsonValueKind.Object)
                return null;
            if (!sdk.TryGetProperty("version", out var version) || version.GetString() is not { } named || !IsVersion(named))
                return null;

            string rule = sdk.TryGetProperty("rollForward", out var rollForward) && rollForward.GetString() is { Length: > 0 } word ? word : "latestPatch";

            return new SdkWish(named, rule);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether a word is a version as dotnet spells one: <c>10.0.302</c>, <c>10.0.100-rc.1.25451.107</c>.</summary>
    public static bool IsVersion(string word) => VersionWord().IsMatch(word);

    /// <summary>The numbers of a version, the prerelease suffix left out; null for a word that is not a version.</summary>
    public static Version? Parsed(string word)
    {
        string numbers = word.Split('-', 2)[0];

        return System.Version.TryParse(numbers, out var version) ? version : null;
    }

    /// <summary>Whether one of <paramref name="installed"/> meets the wish under its roll-forward rule.</summary>
    public bool IsMetBy(IReadOnlyList<string> installed)
    {
        if (installed.Contains(Version, StringComparer.Ordinal))
            return true;
        if (Parsed(Version) is not { } wanted)
            return false;

        var candidates = installed.Select(Parsed).OfType<Version>().Where(version => version >= wanted);

        return RollForward.ToLowerInvariant() switch
        {
            "disable" => false,
            "patch" or "latestpatch" => candidates.Any(version => version.Major == wanted.Major && version.Minor == wanted.Minor && version.Build / 100 == wanted.Build / 100),
            "feature" or "latestfeature" => candidates.Any(version => version.Major == wanted.Major && version.Minor == wanted.Minor),
            "minor" or "latestminor" => candidates.Any(version => version.Major == wanted.Major),
            "major" or "latestmajor" => candidates.Any(),
            _ => false
        };
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$")]
    private static partial Regex VersionWord();
}
