namespace Snail.MCP.BuildBridge.Domain;

/// <summary>What a host must have under its root to build: the .NET SDK, its workloads, the folders, and the package feeds mirrored from this machine.</summary>
/// <param name="SdkVersion">The .NET SDK installed under the root; empty leaves the host's own dotnet to be used.</param>
/// <param name="Workloads">The dotnet workloads that SDK gets, such as maui-ios.</param>
/// <param name="Feeds">The names of the NuGet feeds mirrored under the root's feeds folder, each added to the host's NuGet sources by that name.</param>
/// <param name="Images">The images built on the host for the kinds that run in a container, by name: dotnet, android.</param>
/// <param name="HasXcodeGen">Whether XcodeGen is put under the root where Xcode is, for projects generated from a spec.</param>
/// <param name="XcodeGenVersion">The XcodeGen release fetched, so every host runs the same one.</param>
/// <param name="XcodeGenSha256">The SHA-256 of that release's zip, checked before it is unpacked; empty checks nothing.</param>
/// <remarks>The Android toolchain is the init properties: whether it goes under the root, which JDK and command-line tools, with their checksums, and which SDK packages.</remarks>
public sealed record BuilderManifest(string SdkVersion, IReadOnlyList<string> Workloads, IReadOnlyList<string> Feeds, IReadOnlyList<string> Images, bool HasXcodeGen = true, string XcodeGenVersion = BuilderManifest.DefaultXcodeGenVersion, string XcodeGenSha256 = BuilderManifest.DefaultXcodeGenSha256)
{
    /// <summary>The XcodeGen release the settings name unless they say otherwise.</summary>
    public const string DefaultXcodeGenVersion = "2.46.0";

    /// <summary>The SHA-256 of that release's zip, measured on 2026-11-03.</summary>
    public const string DefaultXcodeGenSha256 = "4d9e34b62172d645eed6457cac13fc222569974098ef4ee9c3368bedf0196806";

    /// <summary>The Temurin release the settings name unless they say otherwise: the current build of the 21 LTS on 2026-11-03.</summary>
    public const string DefaultJdkVersion = "21.0.13+9";

    /// <summary>The SHA-256 of that release's macOS arm64 tarball, as Adoptium publishes it and as measured on 2026-11-03.</summary>
    public const string DefaultJdkSha256 = "bf036b35b071bfb2f54f2c0af8ee8d88afa95ee6eb3dce02c0f94cfc2524d04a";

    /// <summary>The build of the Android command-line tools the settings name unless they say otherwise, the same the Android image carries.</summary>
    public const string DefaultAndroidToolsBuild = "11076708";

    /// <summary>The SHA-256 of that build's macOS zip, measured on 2026-11-03.</summary>
    public const string DefaultAndroidToolsSha256 = "7bc5c72ba0275c80a8f19684fb92793b83a6b5c94d4d179fc5988930282d7e64";

    /// <summary>The SDK packages sdkmanager installs unless the settings say otherwise: the platform tools, the two latest platforms, their build tools, and the NDK r28c.</summary>
    public static IReadOnlyList<string> DefaultAndroidPackages { get; } = ["platform-tools", "platforms;android-36", "platforms;android-35", "build-tools;36.0.0", "ndk;28.2.13676358"];

    /// <summary>Whether a JDK and the Android SDK go under the root, for the gradle kind and for an Android build on the host itself.</summary>
    public bool HasAndroid { get; init; } = true;

    /// <summary>The Temurin release fetched, as Adoptium names it: <c>21.0.13+9</c>.</summary>
    public string JdkVersion { get; init; } = DefaultJdkVersion;

    /// <summary>The SHA-256 of that release's macOS arm64 tarball, checked before it is unpacked; empty checks nothing.</summary>
    public string JdkSha256 { get; init; } = DefaultJdkSha256;

    /// <summary>The build of the Android command-line tools fetched, as the file is named: <c>11076708</c>.</summary>
    public string AndroidToolsBuild { get; init; } = DefaultAndroidToolsBuild;

    /// <summary>The SHA-256 of that build's macOS zip, checked before it is unpacked; empty checks nothing.</summary>
    public string AndroidToolsSha256 { get; init; } = DefaultAndroidToolsSha256;

    /// <summary>The SDK packages sdkmanager installs, as it names them.</summary>
    public IReadOnlyList<string> AndroidPackages { get; init; } = DefaultAndroidPackages;
}

/// <summary>The folders every root has, and the rest of what a manifest knows that is not a parameter.</summary>
public static class BuilderManifests
{
    /// <summary>The folders every root has.</summary>
    public static IReadOnlyList<string> Folders { get; } = ["bin", "workspaces", "jobs", "nuget", "derived", "feeds", "images", "containers", "jdk", "android", "gradle"];

    /// <summary>Where a XcodeGen release is fetched from, by version.</summary>
    public static string XcodeGenUrlOf(string version) => $"https://github.com/yonaskolb/XcodeGen/releases/download/{version}/xcodegen.zip";

    /// <summary>Where a Temurin JDK for macOS arm64 is fetched from, by the version Adoptium names it: the tag carries the plus, the file an underscore.</summary>
    public static string JdkUrlOf(string version)
    {
        string major = version.Split('.')[0];
        string tag = Uri.EscapeDataString($"jdk-{version}");
        string file = $"OpenJDK{major}U-jdk_aarch64_mac_hotspot_{version.Replace('+', '_')}.tar.gz";

        return $"https://github.com/adoptium/temurin{major}-binaries/releases/download/{tag}/{file}";
    }

    /// <summary>Where the Android command-line tools for macOS are fetched from, by build.</summary>
    public static string AndroidToolsUrlOf(string build) => $"https://dl.google.com/android/repository/commandlinetools-mac-{build}_latest.zip";
}

/// <summary>An image a host builds for a kind that runs in a container: the toolchain of the case, made once and reused by every run.</summary>
/// <remarks>
/// A folder is cheaper than a container, so the host builds dotnet, xcodebuild, gradle and docker in its own folders; a
/// container is paid for where the host cannot do the work at all — a Linux target, Native AOT for Linux — or, for an
/// Android build, where the toolchain is not under the root. The Android image is amd64: its SDK's tools for Linux come
/// for that architecture, so on an Apple Silicon host it runs under emulation, and an Android build there is slow by
/// design, which is why the toolchain under the root is the first choice and the image is not built unless asked for.
/// </remarks>
public static class BuildImage
{
    /// <summary>The image of the Linux case: the .NET SDK with what Native AOT for Linux needs.</summary>
    public const string Dotnet = "dotnet";

    /// <summary>The image of the Android case on a host without the toolchain: the .NET SDK, the JDK, the Android SDK and the maui-android workload.</summary>
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
