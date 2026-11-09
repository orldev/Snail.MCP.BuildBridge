using Microsoft.Extensions.Options;
using Snail.MCP.BuildBridge.Application;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Configuration;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>The secrets the settings name, valued from the settings file or from this machine's keychain through the security command.</summary>
/// <remarks>A generic password is read with <c>security find-generic-password -s &lt;service&gt; -w</c>, which answers the password alone; a service the keychain does not hold fails the run before anything reaches the host.</remarks>
public sealed class KeychainSecrets(IPrograms programs, IOptions<BuildOptions> options) : ISecrets
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <inheritdoc/>
    public async Task<Answer<IReadOnlyDictionary<string, string>>> ForAsync(string repository, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var secret in options.Value.Secrets.Where(secret => secret.IsFor(repository)))
        {
            var read = await ValueOfAsync(secret, cancellationToken);
            if (!read.TryGet(out var value))
                return read.As<IReadOnlyDictionary<string, string>>();

            variables[secret.Name] = value;
        }

        return Answer.Ok<IReadOnlyDictionary<string, string>>(variables);
    }

    private async Task<Answer<string>> ValueOfAsync(SecretSetting secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(secret.Keychain))
            return Answer.Ok(secret.Value ?? string.Empty);

        var run = await programs.RunAsync("security", ["find-generic-password", "-s", secret.Keychain, "-w"], null, Patience, cancellationToken);
        if (!run.IsSuccess)
            return Answer.Failed(Messages.SecretUnread(secret.Name, secret.Keychain, run.IsMissing ? "security is not on this machine" : run.Error.Trim()), Messages.SecretHint(secret.Keychain), Refusals.NoSecret);

        return Answer.Ok(run.Output.TrimEnd('\n', '\r'));
    }
}
