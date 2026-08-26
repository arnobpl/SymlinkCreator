namespace SymlinkCreator.Application.Core;

internal static class BatchScriptSyntax
{
    // Wraps a path in double quotes for a generated batch file rather than a native process
    // argument.
    // Double quotes protect special cmd.exe characters such as &, while percent signs still
    // require doubling even inside double quotes. Current callers disable delayed expansion so
    // exclamation marks in valid Windows paths remain literal as well.
    public static bool TryQuote(string? value, out string quotedValue)
    {
        if (value is null || value.Contains('"') || value.Contains('\r') || value.Contains('\n'))
        {
            quotedValue = string.Empty;
            return false;
        }

        // Percent signs are expanded by cmd.exe in batch files, even inside double quotes.
        // Replacing each literal percent sign with %% preserves it in the command argument.
        quotedValue = $"\"{value.Replace("%", "%%", StringComparison.Ordinal)}\"";
        return true;
    }
}
