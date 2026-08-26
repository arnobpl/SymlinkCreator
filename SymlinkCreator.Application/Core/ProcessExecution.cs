using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SymlinkCreator.Application.Core;

public sealed record ProcessExecutionResult(int ExitCode, string StandardError, bool WasCancelled = false)
{
    public const string CancellationMessage = "The elevation request was canceled.";
}

public interface IPrivilegedProcessRunner
{
    public Task<ProcessExecutionResult> RunAsync(
        string scriptPath,
        CancellationToken cancellationToken);
}

public sealed class ElevatedScriptRunner(ScriptWorkspace workspace) : IPrivilegedProcessRunner
{
    private const int ErrorCancelled = 1223;
    private readonly ScriptWorkspace _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

    public async Task<ProcessExecutionResult> RunAsync(
        string scriptPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCancelledResult();
        }

        // "runas" requires shell execution, so standard-error cannot be redirected
        // through ProcessStartInfo. The temporary elevated batch wrapper performs
        // that redirection and forwards the generated script's exit code.
        string wrapperPath = _workspace.CreateTemporaryPath("_wrapper.cmd");
        string standardErrorPath = _workspace.CreateTemporaryPath("_stderr.txt");
        Process? process = null;

        try
        {
            // The wrapper selects code page 65001 explicitly; omit a UTF-8 BOM so cmd.exe
            // sees the first batch command exactly as emitted.
            await File.WriteAllTextAsync(
                standardErrorPath,
                string.Empty,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            await File.WriteAllTextAsync(
                wrapperPath,
                CreateWrapperScript(scriptPath, standardErrorPath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            process = Process.Start(new ProcessStartInfo
            {
                FileName = wrapperPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = _workspace.RootDirectory
            });

            if (process is null)
            {
                return new ProcessExecutionResult(-1, "The elevated script process could not be started.");
            }

            // If cancellation interrupts the wait, the catch block stops the elevated child
            // before the per-run wrapper files are removed in finally.
            await process.WaitForExitAsync(cancellationToken);

            string standardError = await File.ReadAllTextAsync(standardErrorPath, cancellationToken);
            return new ProcessExecutionResult(process.ExitCode, standardError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bool processStopped = process is null || await StopProcessAsync(process);
            return processStopped
                ? CreateCancelledResult()
                : new ProcessExecutionResult(
                    -1,
                    "The elevation request was canceled, but Windows did not permit the elevated process to be stopped.",
                    WasCancelled: true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ProcessExecutionResult(-1, ex.Message, WasCancelled: true);
        }
        finally
        {
            process?.Dispose();
            ScriptWorkspace.DeleteIfExists(wrapperPath);
            ScriptWorkspace.DeleteIfExists(standardErrorPath);
        }
    }

    private static ProcessExecutionResult CreateCancelledResult()
    {
        return new ProcessExecutionResult(
            -1,
            ProcessExecutionResult.CancellationMessage,
            WasCancelled: true);
    }

    private static async Task<bool> StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                // Cleanup must not be canceled with the caller's token; otherwise the child
                // could remain running after RunAsync has reported cancellation.
                await process.WaitForExitAsync(CancellationToken.None);
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // The process can exit between HasExited and Kill.
            return true;
        }
        catch (Win32Exception)
        {
            // A non-elevated caller may not be allowed to terminate an elevated process.
            return false;
        }
    }

    internal static string CreateWrapperScript(string scriptPath, string standardErrorPath)
    {
        return string.Join(
            Environment.NewLine,
            "@echo off",
            "setlocal DisableDelayedExpansion",
            "chcp 65001 >NUL",
            $"call {Quote(scriptPath)} 2> {Quote(standardErrorPath)}",
            // CALL returns from the generated batch file so its error level can be forwarded.
            "exit /b %errorlevel%",
            string.Empty);
    }

    private static string Quote(string path)
    {
        return BatchScriptSyntax.TryQuote(path, out string quotedPath)
            ? quotedPath
            : throw new ArgumentException("A script path contains invalid characters.", nameof(path));
    }
}
