using System.Collections;
using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.BuildBridge.Configuration;

/// <summary>The settings <see cref="BuildOptions"/> is bound from: the server's settings file, then its <c>SNAIL_MCP_BUILDBRIDGE_</c> variables, which win.</summary>
public static class BuildSettings
{
    /// <summary>The server's short name, which names its settings file, its data folder and the prefix of its variables.</summary>
    public const string Name = "buildbridge";

    /// <summary>The settings as they stand: the settings file found, then the process's variables.</summary>
    public static IConfiguration Read() => Read(ServerSettings.FileOf(Name), Environment.GetEnvironmentVariables());

    /// <summary>The settings of <paramref name="file"/>, when there is one, under the server's variables among <paramref name="variables"/>.</summary>
    public static IConfiguration Read(string? file, IDictionary variables) =>
        new ConfigurationBuilder()
            .AddServerSettings(Name, file, variables)
            .Build();
}
