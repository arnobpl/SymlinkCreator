namespace SymlinkCreator.Core;

public static class PathInput
{
    public static string Sanitize(string? path)
    {
        return (path ?? string.Empty).Trim().Trim('"');
    }

    public static IReadOnlyList<string> ParseLines(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : (IReadOnlyList<string>)[
            .. value
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(Sanitize)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
        ];
    }

    public static bool EntryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.Exists(path);
    }

    public static bool FileOrDirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path) || Directory.Exists(path);
    }

    public static string? FindFirstMissingFileOrDirectory(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.FirstOrDefault(static path => !FileOrDirectoryExists(path));
    }
}
