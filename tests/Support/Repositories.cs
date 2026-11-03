using System.Diagnostics;
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.BuildBridge.Tests.Support;

/// <summary>Git repositories a test makes for itself: one commit, one ignored folder with a file in it, one untracked file.</summary>
internal static class Repositories
{
    /// <summary>A repository named <paramref name="name"/> in a test folder of its own, on branch main.</summary>
    public static string Create(string name = "Sample")
    {
        string folder = TestFolder.Create();
        string repository = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.Combine(repository, "src", "bin"));
        Directory.CreateDirectory(Path.Combine(repository, "secrets"));

        File.WriteAllText(Path.Combine(repository, ".gitignore"), "bin/\nsecrets/\n");
        File.WriteAllText(Path.Combine(repository, "README.md"), "# Sample\n");
        File.WriteAllText(Path.Combine(repository, "src", "Program.cs"), "// program\n");
        File.WriteAllText(Path.Combine(repository, "src", "bin", "out.dll"), "built\n");
        File.WriteAllText(Path.Combine(repository, "secrets", "key.txt"), "hidden\n");
        File.WriteAllText(Path.Combine(repository, "notes.txt"), "untracked\n");

        Git(repository, "init", "-q", "-b", "main");
        Git(repository, "-c", "user.name=test", "-c", "user.email=test@example.com", "add", "README.md", "src/Program.cs", ".gitignore");
        Git(repository, "-c", "user.name=test", "-c", "user.email=test@example.com", "commit", "-q", "-m", "first");

        return repository;
    }

    /// <summary>Runs git in <paramref name="repository"/> and returns what it wrote.</summary>
    public static string Git(string repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = repository };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output.Trim();
    }
}
