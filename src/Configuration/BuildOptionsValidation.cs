using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Domain;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.BuildBridge.Configuration;

/// <summary>What makes the settings unusable, said at start rather than met in a tool: a host without a name or an address, two hosts of one name, a time of zero, a sync nobody knows, a secret without a name or a source.</summary>
/// <remarks>
/// Each problem names the variable the operator sets and its key in the settings file, the value it has and what it can
/// be, one sentence each, as the fleet's standard asks of a server's own validation:
/// <c>SNAIL_MCP_BUILDBRIDGE_RUN_SECONDS (or RunSeconds in the settings file) is '0'; it is a number of seconds above zero.</c>
/// The messages named the keys of the settings file alone, and a person who had set a variable was left to guess which.
/// A secret's value is never said back, only that it is set.
/// </remarks>
public sealed class BuildOptionsValidation : IValidateOptions<BuildOptions>
{
    /// <summary>Every problem of <paramref name="options"/>, in words; none when they are usable.</summary>
    public static IReadOnlyList<string> ProblemsOf(BuildOptions options)
    {
        var problems = new List<string>();
        var firstOfName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < options.Hosts.Length; index++)
        {
            var host = options.Hosts[index];
            if (string.IsNullOrWhiteSpace(host.Name))
                problems.Add($"{SettingOf($"Hosts:{index}:Name")} {Said(host.Name)}; it is the word a call names the host by.");
            if (string.IsNullOrWhiteSpace(host.Address))
                problems.Add($"{SettingOf($"Hosts:{index}:Address")} {Said(host.Address)}; it is what ssh is given for the host {host.Name}: an alias from the SSH configuration, or user@host.");
            if (string.IsNullOrWhiteSpace(host.Root))
                problems.Add($"{SettingOf($"Hosts:{index}:Root")} {Said(host.Root)}; it is the folder on the host {host.Name} everything of this server lives under, {Host.DefaultRoot} unless set.");
            if (string.IsNullOrWhiteSpace(host.Name) || firstOfName.TryAdd(host.Name, index))
                continue;

            var first = firstOfName[host.Name];
            problems.Add($"{SettingOf($"Hosts:{index}:Name")} {Said(host.Name)}; it is a name no other host has, case aside, and Hosts:{first}:Name is '{options.Hosts[first].Name}'.");
        }

        AddSecondsProblem(problems, nameof(BuildOptions.TransferSeconds), options.TransferSeconds);
        AddSecondsProblem(problems, nameof(BuildOptions.RunSeconds), options.RunSeconds);
        AddSecondsProblem(problems, nameof(BuildOptions.ProbeSeconds), options.ProbeSeconds);
        if (!options.HasKnownSync)
            problems.Add($"{SettingOf(nameof(BuildOptions.Sync))} {Said(options.Sync)}; it is {Delivery.Rsync} or {Delivery.Mutagen}.");

        for (var index = 0; index < options.Secrets.Length; index++)
        {
            var secret = options.Secrets[index];
            if (!Variables.IsName(secret.Name))
                problems.Add($"{SettingOf($"Secrets:{index}:Name")} {Said(secret.Name)}; it is named as a variable is: letters, digits and underscores, not starting with a digit.");

            var hasValue = !string.IsNullOrEmpty(secret.Value);
            var hasKeychain = !string.IsNullOrEmpty(secret.Keychain);
            if (hasValue && hasKeychain)
                problems.Add($"{SettingOf($"Secrets:{index}:Value")} is set and {SettingOf($"Secrets:{index}:Keychain")} is '{secret.Keychain}' for the secret {secret.Name}; it takes one of the two, a value or the keychain service of a generic password.");
            if (!hasValue && !hasKeychain)
                problems.Add($"{SettingOf($"Secrets:{index}:Value")} and {SettingOf($"Secrets:{index}:Keychain")} are both empty for the secret {secret.Name}; it takes one of the two, a value or the keychain service of a generic password.");
        }

        return problems;
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, BuildOptions options)
    {
        var problems = ProblemsOf(options);

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    /// <summary>A setting as the operator sets it: its variable, and its key in the settings file.</summary>
    private static string SettingOf(string key) => ServerSettings.NameOfSetting(BuildSettings.Name, key);

    /// <summary>What a setting holds, as a sentence says it: <c>is 'scp'</c>, or <c>is empty</c>.</summary>
    private static string Said(string? value) => string.IsNullOrEmpty(value) ? "is empty" : $"is '{value}'";

    /// <summary>Adds the problem of a time that is not above zero.</summary>
    private static void AddSecondsProblem(List<string> problems, string key, int seconds)
    {
        if (seconds <= 0)
            problems.Add($"{SettingOf(key)} is '{seconds}'; it is a number of seconds above zero.");
    }
}
