using System.ComponentModel;
using System.Diagnostics;
using Snail.MCP.BuildBridge.Application.Ports;
using Snail.MCP.BuildBridge.Domain;

namespace Snail.MCP.BuildBridge.Adapters;

/// <summary>Runs outside programs with a deadline and the C locale.</summary>
/// <remarks>
/// A program that is not installed is an answer, not an exception. The C locale keeps git's and rsync's words in
/// English on a machine set to another language, which the parsing here depends on.
/// </remarks>
public sealed class Programs : IPrograms
{
    /// <inheritdoc/>
    public async Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, string? directory, TimeSpan timeout, CancellationToken cancellationToken, string? input = null)
    {
        using var process = new Process { StartInfo = Started(program, arguments, directory) };
        if (!TryStart(process))
            return ProgramRun.Missing(program);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);

        try
        {
            if (input is not null)
                await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();

            await process.WaitForExitAsync(deadline.Token);

            return new ProgramRun(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Stop(process);

            return new ProgramRun(-1, string.Empty, string.Empty, IsTimedOut: true);
        }
        finally
        {
            Stop(process);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The program's errors are joined to its output by a shell before anything is read, so one stream goes to the log
    /// in the order it was written; the file is written unbuffered, so a reader sees each chunk as it lands.
    /// </remarks>
    public async Task<ProgramRun> RunToLogAsync(string program, IReadOnlyList<string> arguments, string? directory, string log, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);

        using var process = new Process { StartInfo = Started("/bin/sh", ["-c", "exec \"$@\" 2>&1", "sh", program, .. arguments], directory) };
        if (!TryStart(process))
            return ProgramRun.Missing(program);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            await using (var file = new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 1))
                await process.StandardOutput.BaseStream.CopyToAsync(file, deadline.Token);

            await process.WaitForExitAsync(deadline.Token);

            return new ProgramRun(process.ExitCode, string.Empty, string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Stop(process);

            return new ProgramRun(-1, string.Empty, string.Empty, IsTimedOut: true);
        }
        finally
        {
            Stop(process);
        }
    }

    private static bool TryStart(Process process)
    {
        try
        {
            return process.Start();
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static void Stop(Process process)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
    }

    private static ProcessStartInfo Started(string program, IReadOnlyList<string> arguments, string? directory)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        if (directory is not null)
            start.WorkingDirectory = directory;

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["LC_ALL"] = "C";
        start.Environment["LANG"] = "C";

        return start;
    }
}
