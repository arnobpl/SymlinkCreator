using System.Text;

namespace SymlinkCreator.Application.Core;

public sealed class ScriptWorkspace(
    string? rootDirectory = null,
    string? retainedScriptDirectory = null)
{
    public string RootDirectory { get; } = string.IsNullOrWhiteSpace(rootDirectory)
            ? GetDefaultRootDirectory()
            : Path.GetFullPath(rootDirectory);

    public string RetainedScriptDirectory { get; } = GetRetainedScriptDirectory(
        rootDirectory,
        retainedScriptDirectory);

    public string CreateScriptPath(bool retainScript)
    {
        string directory = retainScript ? RetainedScriptDirectory : RootDirectory;
        _ = Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{ApplicationMetadata.FileName}_{Guid.NewGuid():N}.cmd");
    }

    public string CreateTemporaryPath(string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        _ = Directory.CreateDirectory(RootDirectory);
        return Path.Combine(RootDirectory, $"{ApplicationMetadata.FileName}_{Guid.NewGuid():N}{suffix}");
    }

    public void WriteScript(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        _ = Directory.CreateDirectory(RootDirectory);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void DeleteIfExists(string path)
    {
        File.Delete(path);
    }

    private static string GetDefaultRootDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, ApplicationMetadata.FileName, "Scripts");
    }

    private static string GetRetainedScriptDirectory(
        string? rootDirectory,
        string? retainedScriptDirectory)
    {
        if (!string.IsNullOrWhiteSpace(retainedScriptDirectory))
        {
            return Path.GetFullPath(retainedScriptDirectory);
        }

        if (!string.IsNullOrWhiteSpace(rootDirectory))
        {
            return Path.GetFullPath(rootDirectory);
        }

        string desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.IsNullOrWhiteSpace(desktopDirectory)
            ? throw new InvalidOperationException("The current user's Desktop directory is unavailable.")
            : desktopDirectory;
    }
}
