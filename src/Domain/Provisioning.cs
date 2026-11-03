namespace Snail.MCP.BuildBridge.Domain;

/// <summary>What a host must have under its root to build: the .NET SDK, its workloads, the folders, and the package feeds mirrored from this machine.</summary>
/// <param name="SdkVersion">The .NET SDK installed under the root; empty leaves the host's own dotnet to be used.</param>
/// <param name="Workloads">The dotnet workloads that SDK gets, such as maui-ios.</param>
/// <param name="Feeds">The names of the NuGet feeds mirrored under the root's feeds folder, each added to the host's NuGet sources by that name.</param>
/// <param name="Images">The images built on the host for the kinds that run in a container, by name: dotnet, android.</param>
/// <param name="HasXcodeGen">Whether XcodeGen is put under the root where Xcode is, for projects generated from a spec.</param>
public sealed record BuilderManifest(string SdkVersion, IReadOnlyList<string> Workloads, IReadOnlyList<string> Feeds, IReadOnlyList<string> Images, bool HasXcodeGen = true)
{
    /// <summary>The folders every root has.</summary>
    public static IReadOnlyList<string> Folders { get; } = ["bin", "workspaces", "jobs", "nuget", "derived", "feeds", "images", "containers"];
}

/// <summary>An image a host builds for a kind that runs in a container: the toolchain of the case, made once and reused by every run.</summary>
/// <remarks>
/// A folder is cheaper than a container, so the host builds dotnet, xcodebuild and docker in its own folders; a container
/// is paid for where the host cannot do the work at all — a Linux target, Native AOT for Linux — or where the toolchain
/// is better kept apart, as Android's SDK and JDK are. The Android image is amd64: the Android SDK's tools come for
/// that architecture, so on an Apple Silicon host it runs under emulation, and an Android build there is slow by design.
/// </remarks>
public static class BuildImage
{
    /// <summary>The image of the Linux case: the .NET SDK with what Native AOT for Linux needs.</summary>
    public const string Dotnet = "dotnet";

    /// <summary>The image of the Android case: the .NET SDK, the JDK, the Android SDK and the maui-android workload.</summary>
    public const string Android = "android";

    /// <summary>The prefix every image's tag carries; what follows is the name, then the SDK version.</summary>
    public const string Prefix = "s3l-build-";

    /// <summary>The platform an image is built and run for; null where the host's own will do.</summary>
    public static string? PlatformOf(string name) => name == Android ? "linux/amd64" : null;

    /// <summary>The tag an image is built under on a host: <c>s3l-build-&lt;name&gt;:&lt;sdk&gt;</c>, or <c>latest</c> when no SDK version is named.</summary>
    public static string TagOf(string name, string sdkVersion) => $"{Prefix}{name}:{(sdkVersion.Length > 0 ? sdkVersion : "latest")}";
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
