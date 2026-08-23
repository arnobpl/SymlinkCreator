using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SymlinkCreator.Application.Core;

public sealed record ProcessExecutionResult(int ExitCode, string StandardError, bool WasCancelled = false);

public interface IPrivilegedProcessRunner
{
    public ProcessExecutionResult Run(string scriptPath);
}

public sealed class ElevatedScriptRunner(ScriptWorkspace workspace) : IPrivilegedProcessRunner
{
    private const int ErrorCancelled = 1223;
    private readonly ScriptWorkspace _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

    public ProcessExecutionResult Run(string scriptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        string wrapperPath = _workspace.CreateTemporaryPath("_wrapper.cmd");
        string standardErrorPath = _workspace.CreateTemporaryPath("_stderr.txt");

        try
        {
            File.WriteAllText(
                standardErrorPath,
                string.Empty,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(
                wrapperPath,
                CreateWrapperScript(scriptPath, standardErrorPath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            using var process = Process.Start(new ProcessStartInfo
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

            process.WaitForExit();
            string standardError = File.ReadAllText(standardErrorPath);
            return new ProcessExecutionResult(process.ExitCode, standardError);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ProcessExecutionResult(-1, ex.Message, WasCancelled: true);
        }
        finally
        {
            ScriptWorkspace.DeleteIfExists(wrapperPath);
            ScriptWorkspace.DeleteIfExists(standardErrorPath);
        }
    }

    private static string CreateWrapperScript(string scriptPath, string standardErrorPath)
    {
        return string.Join(
            Environment.NewLine,
            "@echo off",
            "setlocal DisableDelayedExpansion",
            "chcp 65001 >NUL",
            $"call {Quote(scriptPath)} 2> {Quote(standardErrorPath)}",
            "exit /b %errorlevel%",
            string.Empty);
    }

    private static string Quote(string path)
    {
        return path.Contains('"') || path.Contains('\r') || path.Contains('\n')
            ? throw new ArgumentException("A script path contains invalid characters.", nameof(path))
            : $"\"{path.Replace("%", "%%", StringComparison.Ordinal)}\"";
    }
}
