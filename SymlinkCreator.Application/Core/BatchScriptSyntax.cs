namespace SymlinkCreator.Application.Core;

internal static class BatchScriptSyntax
{
    public static bool TryQuote(string? value, out string quotedValue)
    {
        if (value is null || value.Contains('"') || value.Contains('\r') || value.Contains('\n'))
        {
            quotedValue = string.Empty;
            return false;
        }

        // Percent signs are expanded by cmd.exe in batch files, even inside quotes.
        // Doubling them preserves literal percent signs in command arguments.
        quotedValue = $"\"{value.Replace("%", "%%", StringComparison.Ordinal)}\"";
        return true;
    }
}
