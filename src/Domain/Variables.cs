using System.Text.RegularExpressions;

namespace Snail.MCP.BuildBridge.Domain;

/// <summary>The variables a run carries into its command's environment: what a call names, and the secrets of this machine.</summary>
public static partial class Variables
{
    /// <summary>Whether a word is a variable's name: letters, digits and underscores, not starting with a digit.</summary>
    public static bool IsName(string word) => Name().IsMatch(word);

    /// <summary>The assignments <c>NAME=value</c> of a call as variables, and the first word that is not one; a name given twice keeps its last value.</summary>
    public static (IReadOnlyDictionary<string, string> Variables, string? Odd) Parsed(IReadOnlyList<string> assignments)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var parts = assignment.Split('=', 2);
            if (parts.Length != 2 || !IsName(parts[0]))
                return (variables, assignment);

            variables[parts[0]] = parts[1];
        }

        return (variables, null);
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Name();
}
