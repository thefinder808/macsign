using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MacSign.App.Services;

/// <summary>The outcome of running an external tool: exit code + captured output.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool Canceled)
{
    public bool Success => !Canceled && ExitCode == 0;
}

/// <summary>
/// Runs an external tool. This is the single seam every Apple-tool call goes
/// through — and the command-injection firewall: arguments are passed as an argv
/// list (never a shell command string), so file paths / identities / profile
/// names can never inject shell syntax. The interface is also the test seam.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args,
        IProgress<string>? onOutput, CancellationToken ct);
}

/// <summary>
/// The real <see cref="IProcessRunner"/>: no shell, asynchronous (non-deadlocking)
/// stdout/stderr reads, and cancellation that kills the whole process tree.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args,
        IProgress<string>? onOutput, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,      // exec directly — no /bin/sh, no interpolation
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // Each argument is one opaque argv element passed straight to execv —
        // metacharacters (;, $(), backticks, quotes, spaces) cannot inject.
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            onOutput?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            onOutput?.Report(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            // Tool missing / not executable — report rather than throw (callers
            // are fed hostile-ish input and rely on a result, never an exception).
            return new ProcessResult(-1, "", ex.Message, false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool canceled = false;
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        // Flush the async readers (also waits out a just-killed process).
        try { process.WaitForExit(); } catch { /* best-effort */ }

        return new ProcessResult(canceled ? -1 : process.ExitCode,
            stdout.ToString(), stderr.ToString(), canceled);
    }
}
