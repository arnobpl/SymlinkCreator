using System.Diagnostics;

namespace SymlinkCreator.Tests;

internal sealed record ProcessTestResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ProcessTestHelper
{
    public static async Task<ProcessTestResult> RunBatchAsync(
        string scriptPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(scriptPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The batch script could not be started.");
        // Drain both redirected streams concurrently so a full pipe cannot block the child
        // before it reaches process exit.
        Task<string[]> outputTask = Task.WhenAll(
            process.StandardOutput.ReadToEndAsync(cancellationToken),
            process.StandardError.ReadToEndAsync(cancellationToken));
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    // Cleanup must finish even when the test's cancellation token has fired,
                    // so the test run does not leave a child process behind.
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            finally
            {
                try
                {
                    await outputTask;
                }
                catch when (cancellationToken.IsCancellationRequested)
                {
                    // Observe cancellation from both redirected-output readers before propagating it.
                }
            }
        }

        string[] output = await outputTask;
        return new ProcessTestResult(process.ExitCode, output[0], output[1]);
    }
}
