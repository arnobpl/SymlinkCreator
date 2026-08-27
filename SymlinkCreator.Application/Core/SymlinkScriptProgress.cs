namespace SymlinkCreator.Application.Core;

public sealed record SymlinkScriptProgress(
    int? FailedEntryIndex = null,
    int? SuccessfulEntryCount = null);

internal static class SymlinkScriptProgressParser
{
    public const string EntryAttemptPrefix = "SYMLINKCREATOR_ENTRY_ATTEMPT:";
    public const string EntrySuccessPrefix = "SYMLINKCREATOR_ENTRY_SUCCESS:";

    public static (string StandardError, SymlinkScriptProgress Progress) Parse(
        int exitCode,
        string standardError,
        int expectedEntryCount)
    {
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedEntryCount);

        string[] lines = standardError.Split(["\r\n", "\n"], StringSplitOptions.None);
        var cleanLines = new List<string>(lines.Length);
        int nextExpectedEntryNumber = 1;
        int? currentEntryNumber = null;
        var successfulEntryNumbers = new HashSet<int>();
        bool hasProgressMarkers = false;

        foreach (string line in lines)
        {
            if (TryReadEntryNumber(line, EntryAttemptPrefix, out int attemptedEntryNumber) &&
                currentEntryNumber is null &&
                attemptedEntryNumber == nextExpectedEntryNumber &&
                attemptedEntryNumber <= expectedEntryCount)
            {
                currentEntryNumber = attemptedEntryNumber;
                nextExpectedEntryNumber++;
                hasProgressMarkers = true;
                continue;
            }

            if (TryReadEntryNumber(line, EntrySuccessPrefix, out int successfulEntryNumber) &&
                currentEntryNumber == successfulEntryNumber &&
                successfulEntryNumbers.Add(successfulEntryNumber))
            {
                currentEntryNumber = null;
                hasProgressMarkers = true;
                continue;
            }

            cleanLines.Add(line);
        }

        return (
            string.Join(Environment.NewLine, cleanLines),
            new SymlinkScriptProgress(
                exitCode == 0 || currentEntryNumber is null
                    ? null
                    : currentEntryNumber.Value - 1,
                hasProgressMarkers ? successfulEntryNumbers.Count : null));
    }

    private static bool TryReadEntryNumber(string line, string prefix, out int entryNumber)
    {
        entryNumber = 0;
        return line.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(line.AsSpan(prefix.Length), out entryNumber) &&
            entryNumber > 0;
    }
}
