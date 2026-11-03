namespace Snail.MCP.BuildBridge.Domain;

/// <summary>What a host must have under its root to build: the .NET SDK, its workloads, the folders, and the package feeds mirrored from this machine.</summary>
/// <param name="SdkVersion">The .NET SDK installed under the root; empty leaves the host's own dotnet to be used.</param>
/// <param name="Workloads">The dotnet workloads that SDK gets, such as maui-ios.</param>
/// <param name="Feeds">The names of the NuGet feeds mirrored under the root's feeds folder, each added to the host's NuGet sources by that name.</param>
public sealed record BuilderManifest(string SdkVersion, IReadOnlyList<string> Workloads, IReadOnlyList<string> Feeds)
{
    /// <summary>The folders every root has.</summary>
    public static IReadOnlyList<string> Folders { get; } = ["bin", "workspaces", "jobs", "nuget", "derived", "feeds"];
}

/// <summary>One thing provisioning saw to: whether it is in place, and a word on it — the version found, or what to do by hand.</summary>
public sealed record ProvisionStep(string Name, bool IsDone, string? Note);

/// <summary>A host as provisioning left it.</summary>
public sealed record Provisioned(Host Host, IReadOnlyList<ProvisionStep> Steps, string Log)
{
    /// <summary>Whether everything is in place, nothing left to do by hand.</summary>
    public bool IsComplete => Steps.All(step => step.IsDone);

    /// <summary>The steps a person has to see to.</summary>
    public IReadOnlyList<ProvisionStep> ByHand => [.. Steps.Where(step => !step.IsDone)];
}
