using System.Text;

namespace SymlinkCreator.Core;

public interface ISymlinkScriptGenerator
{
    public string Generate(SymlinkPlan plan);
}

public sealed class SymlinkScriptGenerator : ISymlinkScriptGenerator
{
    public string Generate(SymlinkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Entries.Count == 0)
        {
            throw new SymlinkValidationException(
                SymlinkValidationError.EmptyPlan,
                "The symlink plan contains no entries.");
        }

        var script = new StringBuilder();
        _ = script.AppendLine("@echo off");
        _ = script.AppendLine("setlocal DisableDelayedExpansion");
        _ = script.AppendLine("chcp 65001 >NUL");
        _ = script.Append("cd /d ").AppendLine(Quote(plan.DestinationDirectory));

        foreach (SymlinkEntry entry in plan.Entries)
        {
            _ = script.Append("mklink ");
            if (entry.IsDirectory)
            {
                _ = script.Append("/d ");
            }

            _ = script.Append(Quote(entry.LinkName));
            _ = script.Append(' ');
            _ = script.AppendLine(Quote(entry.TargetPath));
            _ = script.AppendLine("if errorlevel 1 exit /b %errorlevel%");
        }

        _ = script.AppendLine("exit /b %errorlevel%");
        return script.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Contains('"') || value.Contains('\r') || value.Contains('\n'))
        {
            throw new SymlinkValidationException(
                SymlinkValidationError.GeneratedPathContainsInvalidCharacters,
                "A generated script path contains invalid characters.");
        }

        // Percent signs are expanded in batch files even when surrounded by quotes.
        // Doubling them preserves a literal percent sign for the command being run.
        return $"\"{value.Replace("%", "%%", StringComparison.Ordinal)}\"";
    }
}
