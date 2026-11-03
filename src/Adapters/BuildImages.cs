using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>The Dockerfiles of the images a host builds, carried inside this server so a native build has them on any machine.</summary>
/// <remarks>The files live in <c>deploy/images/&lt;name&gt;/Dockerfile</c> of the repository, where a person can build one by hand, and are embedded from there.</remarks>
public static class BuildImages
{
    /// <summary>The names of the images this server carries a Dockerfile for.</summary>
    public static IReadOnlyList<string> Names { get; } = [BuildImage.Dotnet, BuildImage.Android];

    /// <summary>The Dockerfile of an image, or null when this server carries none by that name.</summary>
    public static string? DockerfileOf(string name)
    {
        using var stream = typeof(BuildImages).Assembly.GetManifestResourceStream($"images/{name}/Dockerfile");
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
