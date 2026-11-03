using System.Text.RegularExpressions;

namespace Snail.MCP.BuildBridge.Domain;

/// <summary>What a run may put under a host's root beyond what provisioning put there, because the build asks for it.</summary>
public enum SupplyKind
{
    /// <summary>Android SDK packages, as sdkmanager names them.</summary>
    AndroidPackages,

    /// <summary>A .NET SDK, by the version a copy's global.json names.</summary>
    Sdk,

    /// <summary>dotnet workloads, by id, for the SDK a copy builds with.</summary>
    Workloads
}

/// <summary>One thing a run puts under the root before it starts, or before it runs again: what kind, which names, and in which copy the dotnet that takes it is to run.</summary>
/// <param name="Kind">What is supplied.</param>
/// <param name="Names">The packages, the version or the workload ids.</param>
/// <param name="Folder">The copy under the root a workload install runs in, so dotnet resolves the SDK its global.json names; null for the root itself.</param>
public sealed record Supply(SupplyKind Kind, IReadOnlyList<string> Names, string? Folder = null)
{
    /// <summary>The turn a supply takes on a host, so two runs do not drive one installer at once: sdkmanager, or dotnet-install and the workloads.</summary>
    public string Turn => Kind == SupplyKind.AndroidPackages ? AndroidPackages.Turn : "dotnet/sdk";

    /// <summary>The supply as a report words it: <c>sdk 10.0.400</c>, <c>workload maui-android</c>, a package as it is.</summary>
    public IReadOnlyList<string> Words => Kind switch
    {
        SupplyKind.Sdk => [.. Names.Select(name => $"sdk {name}")],
        SupplyKind.Workloads => [.. Names.Select(name => $"workload {name}")],
        _ => Names
    };
}

/// <summary>What a failed build said it lacked, read from its log, so the run can put it in place and go again.</summary>
public static partial class Lacks
{
    /// <summary>The supply a log asks for: a platform (XA5207 names the API level) or workloads (NETSDK1147 names their ids); null when the log names nothing this server supplies.</summary>
    public static Supply? In(string log)
    {
        if (AndroidPackages.MissingPlatformIn(log) is { } platform)
            return new Supply(SupplyKind.AndroidPackages, [platform]);

        var workloads = WorkloadsIn(log);

        return workloads.Count > 0 ? new Supply(SupplyKind.Workloads, workloads) : null;
    }

    /// <summary>The workload ids NETSDK1147 names, each once.</summary>
    public static IReadOnlyList<string> WorkloadsIn(string log)
    {
        var named = Workloads().Matches(log).SelectMany(match => match.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries));

        return [.. named.Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"NETSDK1147:.*?must be installed:\s*([a-z0-9][a-z0-9., -]*)")]
    private static partial Regex Workloads();
}
