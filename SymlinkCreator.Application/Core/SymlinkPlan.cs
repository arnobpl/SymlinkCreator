namespace SymlinkCreator.Application.Core;

public sealed record SymlinkRequest(
    IReadOnlyList<string> SourcePaths,
    string DestinationDirectory,
    bool UseRelativePath = true,
    bool RetainScriptFile = false);

public sealed record SymlinkEntry(
    string SourcePath,
    string LinkPath,
    string LinkName,
    string TargetPath,
    bool IsDirectory);

public sealed record SymlinkPlan(
    string DestinationDirectory,
    IReadOnlyList<SymlinkEntry> Entries);

public enum SymlinkValidationError
{
    NoSources,
    DestinationEmpty,
    DestinationNotFound,
    DestinationContainsInvalidCharacters,
    DestinationInvalid,
    SourceEmpty,
    SourceNotFound,
    SourceContainsInvalidCharacters,
    SourceInvalid,
    DuplicateLinkName,
    DestinationEntryExists,
    InvalidLinkName,
    EmptyPlan,
    GeneratedPathContainsInvalidCharacters
}

public sealed class SymlinkValidationException(
    SymlinkValidationError error,
    string message,
    params string[] messageArguments) : ArgumentException(message)
{
    public SymlinkValidationError Error { get; } = error;

    public IReadOnlyList<string> MessageArguments { get; } = Array.AsReadOnly([.. messageArguments]);
}

public sealed class SymlinkExecutionException(string message, int exitCode, bool wasCancelled) : InvalidOperationException(message)
{
    public int ExitCode { get; } = exitCode;

    public bool WasCancelled { get; } = wasCancelled;
}
