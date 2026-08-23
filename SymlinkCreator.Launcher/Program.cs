using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SymlinkCreator.Launcher;

internal static partial class Program
{
    private const uint ErrorMessageBox = 0x00000010;

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string applicationPath = ResolveApplicationPath();
            ProcessStartInfo startInfo = new(applicationPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(applicationPath)!,
            };
            foreach (string argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start Symlink Creator.");
        }
        catch (Exception exception)
        {
            _ = MessageBoxW(
                0,
                $"Symlink Creator could not be started.\n\n{exception.Message}",
                "Symlink Creator",
                ErrorMessageBox);
            Environment.ExitCode = 1;
        }
    }

    private static string ResolveApplicationPath()
    {
        string launcherPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The command launcher path is unavailable.");
        FileSystemInfo? resolvedLauncher = File.ResolveLinkTarget(launcherPath, returnFinalTarget: true);
        string actualLauncherPath = resolvedLauncher?.FullName ?? launcherPath;
        string? applicationDirectory = Path.GetDirectoryName(actualLauncherPath);
        if (string.IsNullOrEmpty(applicationDirectory))
        {
            throw new InvalidOperationException("The Symlink Creator installation directory is unavailable.");
        }

        string applicationPath = Path.Combine(applicationDirectory, "SymlinkCreator.exe");
        if (!File.Exists(applicationPath))
        {
            throw new FileNotFoundException("The installed application executable was not found.", applicationPath);
        }

        return applicationPath;
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint windowHandle, string text, string caption, uint type);
}
