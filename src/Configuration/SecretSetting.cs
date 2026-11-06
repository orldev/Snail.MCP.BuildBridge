namespace Snail.MCP.BuildBridge.Configuration;

/// <summary>One secret a run gets in its environment, from this machine: the variable's name, where its value comes from, and which repository it is for.</summary>
/// <remarks>
/// A secret lives in the settings file or in this machine's keychain, never in a tool's arguments, a job or a log, and
/// never on a host: it reaches the host's shell for the run alone, on its standard input. The model names no secret
/// and sees none; it only learns from build_diagnose which names a run gets.
/// </remarks>
public sealed class SecretSetting
{
    /// <summary>The variable's name in the run's environment.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The value itself, kept in the settings file; empty when the keychain holds it.</summary>
    public string? Value { get; set; }

    /// <summary>The service name of a generic password in this machine's keychain the value is read from; empty when the settings carry it.</summary>
    public string? Keychain { get; set; }

    /// <summary>The repository whose runs get the secret; every repository when empty.</summary>
    public string? Repository { get; set; }

    /// <summary>Whether a run of <paramref name="repository"/> gets this secret.</summary>
    public bool IsFor(string repository) => string.IsNullOrEmpty(Repository) || string.Equals(Repository, repository, StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the value comes from, in a word: value or keychain.</summary>
    public string Source => string.IsNullOrEmpty(Keychain) ? "value" : "keychain";
}
