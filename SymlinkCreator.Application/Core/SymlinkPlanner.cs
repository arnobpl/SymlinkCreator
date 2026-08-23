namespace SymlinkCreator.Application.Core;

public interface ISymlinkPlanner
{
    public SymlinkPlan CreatePlan(
        IEnumerable<string> sourcePaths,
        string destinationDirectory,
        bool useRelativePath = true);
}

public sealed class SymlinkPlanner : ISymlinkPlanner
{
    public SymlinkPlan CreatePlan(
        IEnumerable<string> sourcePaths,
        string destinationDirectory,
        bool useRelativePath = true)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        string[] sanitizedSources =
        [
            .. sourcePaths
                .Select(PathInput.Sanitize)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
        ];

        if (sanitizedSources.Length == 0)
        {
            throw new SymlinkValidationException(
                SymlinkValidationError.NoSources,
                "No source files or folders were provided.");
        }

        string destination = NormalizeExistingDirectory(destinationDirectory, "Destination path");
        List<SymlinkEntry> entries = [];
        var linkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sourcePath in sanitizedSources)
        {
            string source = NormalizeExistingPath(sourcePath, "Source path");
            bool isDirectory = Directory.Exists(source);
            string linkName = GetLinkName(source);

            if (!linkNames.Add(linkName))
            {
                throw new SymlinkValidationException(
                    SymlinkValidationError.DuplicateLinkName,
                    $"Multiple source paths would create the duplicate link name '{linkName}'.",
                    linkName);
            }

            string linkPath = Path.Combine(destination, linkName);
            if (PathInput.EntryExists(linkPath))
            {
                throw new SymlinkValidationException(
                    SymlinkValidationError.DestinationEntryExists,
                    $"The destination already contains '{linkName}'.",
                    linkName);
            }

            string target = source;
            if (useRelativePath && SamePathRoot(source, destination))
            {
                target = Path.GetRelativePath(destination, source);
            }

            entries.Add(new SymlinkEntry(source, linkPath, linkName, target, isDirectory));
        }

        return new SymlinkPlan(destination, entries);
    }

    private static string NormalizeExistingDirectory(string? path, string description)
    {
        string normalized = NormalizeInput(
            path,
            description,
            SymlinkValidationError.DestinationEmpty,
            SymlinkValidationError.DestinationContainsInvalidCharacters,
            SymlinkValidationError.DestinationInvalid);
        return !Directory.Exists(normalized)
            ? throw new SymlinkValidationException(
                SymlinkValidationError.DestinationNotFound,
                $"{description} does not exist: {normalized}",
                normalized)
            : normalized;
    }

    private static string NormalizeExistingPath(string? path, string description)
    {
        string normalized = NormalizeInput(
            path,
            description,
            SymlinkValidationError.SourceEmpty,
            SymlinkValidationError.SourceContainsInvalidCharacters,
            SymlinkValidationError.SourceInvalid);
        return !PathInput.FileOrDirectoryExists(normalized)
            ? throw new SymlinkValidationException(
                SymlinkValidationError.SourceNotFound,
                $"{description} does not exist: {normalized}",
                normalized)
            : normalized;
    }

    private static string NormalizeInput(
        string? path,
        string description,
        SymlinkValidationError emptyError,
        SymlinkValidationError invalidCharactersError,
        SymlinkValidationError invalidError)
    {
        string sanitized = PathInput.Sanitize(path);
        if (sanitized.Length == 0)
        {
            throw new SymlinkValidationException(emptyError, $"{description} is empty.");
        }

        if (sanitized.Contains('"') || sanitized.Contains('\r') || sanitized.Contains('\n'))
        {
            throw new SymlinkValidationException(
                invalidCharactersError,
                $"{description} contains invalid characters.");
        }

        try
        {
            return Path.GetFullPath(sanitized);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            throw new SymlinkValidationException(
                invalidError,
                $"{description} is invalid: {sanitized}",
                sanitized);
        }
    }

    private static string GetLinkName(string source)
    {
        string trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? name = Path.GetFileName(trimmed);

        return string.IsNullOrEmpty(name)
            ? throw new SymlinkValidationException(
                SymlinkValidationError.InvalidLinkName,
                $"The source path cannot be used as a link name: {source}",
                source)
            : name;
    }

    private static bool SamePathRoot(string left, string right)
    {
        string? leftRoot = Path.GetPathRoot(left);
        string? rightRoot = Path.GetPathRoot(right);
        return leftRoot is not null && rightRoot is not null &&
            string.Equals(leftRoot, rightRoot, StringComparison.OrdinalIgnoreCase);
    }

}
