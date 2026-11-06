namespace Snail.MCP.BuildBridge.Domain;

/// <summary>Whether a path a survey read inside a copy may be handed to a shell to erase.</summary>
/// <remarks>
/// The path comes from the host's file system, named by whatever a build wrote, and is erased under the root in double
/// quotes: a dollar, a quote, a backtick or a backslash would be read by the shell, and a <c>..</c> would leave the copy.
/// Such a path is not swept; one a build would write is letters, digits, spaces and <c>. _ - + @</c>.
/// </remarks>
public static class OutputPath
{
    /// <summary>Whether <paramref name="path"/> is relative, stays inside the copy and holds nothing a shell would read.</summary>
    public static bool IsPlain(string path)
    {
        if (path.Length == 0 || path.StartsWith('/') || path.EndsWith('/'))
            return false;

        var segments = path.Split('/');

        return segments.All(segment => segment.Length > 0 && segment is not "." and not "..") && path.All(IsPlainLetter);
    }

    private static bool IsPlainLetter(char letter) => char.IsLetterOrDigit(letter) || letter is '/' or ' ' or '.' or '_' or '-' or '+' or '@';
}
