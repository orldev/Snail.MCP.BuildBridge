using System.Globalization;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>A host's root surveyed: the script that lists what is there, and the survey read from its lines.</summary>
public static class Surveys
{
    /// <summary>The script that surveys a root: a line per workspace with its marker, per derived folder, per SDK under the root and per version a workspace wishes for, per build tools and NDK, per toolchain image, per labelled image, then the build cache, the packages and the room.</summary>
    public static string Script(Host host) =>
        $$$"""
        ROOT={{{RemoteShell.Root(host)}}}
        for marker in "$ROOT"/workspaces/*/*/{{{WorkspaceMarker.FileName}}}; do
          [ -f "$marker" ] || continue
          dir=$(dirname "$marker"); repo=$(basename "$(dirname "$dir")"); branch=$(basename "$dir")
          delivered=$(stat -f %m "$marker"); built=0
          [ -f "$dir/{{{WorkspaceMarker.BuiltFileName}}}" ] && built=$(stat -f %m "$dir/{{{WorkspaceMarker.BuiltFileName}}}")
          echo "s3l-ws $repo $branch $delivered $built $(du -sk "$dir" | cut -f1)"
        done
        for folder in "$ROOT"/derived/*/; do [ -d "$folder" ] && echo "s3l-derived $(basename "$folder")"; done
        for sdk in "$ROOT"/dotnet/sdk/*/; do [ -d "$sdk" ] && echo "s3l-sdk $(basename "$sdk")"; done
        for spec in "$ROOT"/workspaces/*/*/*/{{{SdkWish.FileName}}}; do [ -f "$spec" ] && echo "s3l-sdkwanted $(sed -n 's/.*"version" *: *"\([^"]*\)".*/\1/p' "$spec" | head -n 1)"; done
        for tools in "$ROOT"/android/build-tools/*/; do [ -d "$tools" ] && echo "s3l-buildtools $(basename "$tools")"; done
        for ndk in "$ROOT"/android/ndk/*/; do [ -d "$ndk" ] && echo "s3l-ndk $(basename "$ndk")"; done
        docker image ls --filter label={{{ProvisionScripts.ImageLabel}}} --format 's3l-toolimage {{.Repository}}:{{.Tag}}' 2>/dev/null
        docker image ls --filter label={{{DockerCommand.Label}}} --format 's3l-image {{.ID}} {{.CreatedAt}} {{.Size}}' 2>/dev/null
        docker system df --format '{{.Type}}	{{.Size}}' 2>/dev/null | awk -F '	' '$1 == "Build Cache" { print "s3l-buildcache " $2 }'
        echo "s3l-packages $(du -sk "$ROOT/nuget" "$ROOT/gradle" 2>/dev/null | awk '{ total += $1 } END { print total + 0 }')"
        echo "s3l-free $(df -g "$HOME" | tail -n 1 | awk '{print $4}')"
        """;

    /// <summary>A survey read from the script's lines.</summary>
    public static Survey Of(Host host, string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split(' ')).ToList();
        var workspaces = lines.Where(parts => parts[0] == "s3l-ws" && parts.Length == 6).Select(Workspace).OfType<WorkspaceState>().ToList();
        var derived = lines.Where(parts => parts[0] == "s3l-derived" && parts.Length == 2).Select(parts => parts[1]).ToList();
        var images = lines.Where(parts => parts[0] == "s3l-image" && parts.Length >= 6).Select(Image).OfType<ImageState>().ToList();
        double cache = lines.Where(parts => parts[0] == "s3l-buildcache" && parts.Length == 2).Select(parts => Gigabytes(parts[1])).FirstOrDefault();
        double packages = lines.Where(parts => parts[0] == "s3l-packages" && parts.Length == 2).Select(parts => Kilobytes(parts[1]) / 1024d / 1024d).FirstOrDefault();
        double? free = lines.Where(parts => parts[0] == "s3l-free" && parts.Length == 2).Select(parts => double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : (double?)null).FirstOrDefault();

        return new Survey(host, workspaces, derived, images, cache, packages, free)
        {
            Sdks = Words(lines, "s3l-sdk"),
            SdksWanted = Words(lines, "s3l-sdkwanted"),
            BuildTools = Words(lines, "s3l-buildtools"),
            Ndks = Words(lines, "s3l-ndk"),
            ToolImages = Words(lines, "s3l-toolimage")
        };
    }

    private static IReadOnlyList<string> Words(List<string[]> lines, string tag) =>
        [.. lines.Where(parts => parts[0] == tag && parts.Length == 2).Select(parts => parts[1]).Distinct(StringComparer.Ordinal)];

    private static WorkspaceState? Workspace(string[] parts)
    {
        if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long delivered) || !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long built))
            return null;

        var lastUsed = DateTimeOffset.FromUnixTimeSeconds(Math.Max(delivered, built));

        return new WorkspaceState(new WorkspaceKey(parts[1], parts[2]), lastUsed, Kilobytes(parts[5]) * 1024);
    }

    /// <summary>An image line: the id, the moment as docker writes it — <c>2026-10-30 19:52:13 +0300 MSK</c> — and the size.</summary>
    private static ImageState? Image(string[] parts)
    {
        bool isMoment = DateTime.TryParseExact($"{parts[2]} {parts[3]}", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment);
        if (!isMoment || parts[4].Length != 5)
            return null;
        if (!int.TryParse(parts[4][1..3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours) || !int.TryParse(parts[4][3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes))
            return null;

        var offset = new TimeSpan(hours, minutes, 0) * (parts[4][0] == '-' ? -1 : 1);
        var createdAt = new DateTimeOffset(moment, TimeSpan.Zero) - offset;

        return new ImageState(parts[1], createdAt, parts[^1]);
    }

    private static long Kilobytes(string word) => long.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out long kilobytes) ? kilobytes : 0;

    /// <summary>A size as docker writes it — <c>5.931GB</c>, <c>228MB</c>, <c>0B</c> — in gigabytes.</summary>
    private static double Gigabytes(string size)
    {
        string digits = new([.. size.TakeWhile(letter => char.IsDigit(letter) || letter == '.')]);
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return 0;

        return size[digits.Length..].ToUpperInvariant() switch
        {
            "TB" => number * 1024,
            "GB" => number,
            "MB" => number / 1024,
            "KB" => number / 1024 / 1024,
            _ => 0
        };
    }
}
