using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Configuration;

/// <summary>What makes the settings unusable, said at start rather than met in a tool: a host without a name or an address, two hosts of one name, a time of zero, a sync nobody knows.</summary>
public sealed class BuildOptionsValidation : IValidateOptions<BuildOptions>
{
    /// <summary>Every problem of <paramref name="options"/>, in words; none when they are usable.</summary>
    public static IReadOnlyList<string> ProblemsOf(BuildOptions options)
    {
        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in options.Hosts)
        {
            if (string.IsNullOrWhiteSpace(host.Name))
                problems.Add("A host has no Name.");
            if (string.IsNullOrWhiteSpace(host.Address))
                problems.Add($"The host {host.Name} has no Address.");
            if (string.IsNullOrWhiteSpace(host.Root))
                problems.Add($"The host {host.Name} has an empty Root.");
            if (!string.IsNullOrWhiteSpace(host.Name) && !names.Add(host.Name))
                problems.Add($"Two hosts are called {host.Name}.");
        }
        if (options.TransferSeconds <= 0 || options.RunSeconds <= 0 || options.ProbeSeconds <= 0)
            problems.Add("TransferSeconds, RunSeconds and ProbeSeconds must be above zero.");
        if (!options.HasKnownSync)
            problems.Add($"Sync must be {Delivery.Rsync} or {Delivery.Mutagen}.");

        return problems;
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, BuildOptions options)
    {
        var problems = ProblemsOf(options);

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
