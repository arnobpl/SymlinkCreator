namespace SymlinkCreator.Application.Presentation;

public sealed record StartupOptions(
    bool SuppressElevationWarning = false,
    bool UseRelativePath = true,
    bool RetainScriptFile = false,
    bool HideSuccessfulOperationDialog = false,
    string? Language = null)
{
    private static readonly HashSet<string> SupportedLanguages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "en-US",
            "zh-CN",
            "es",
            "de",
            "fr",
            "bn-BD",
            "ja-JP",
            "pt-BR",
            "ko-KR"
        };

    public static StartupOptions Parse(string? arguments)
    {
        bool suppressElevationWarning = false;
        bool useRelativePath = true;
        bool retainScriptFile = false;
        bool hideSuccessfulOperationDialog = false;
        string? language = null;

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            string[] tokens = arguments.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < tokens.Length; index++)
            {
                string argument = tokens[index];
                string normalizedArgument = argument.ToLowerInvariant();

                if (normalizedArgument.StartsWith("--language=", StringComparison.Ordinal))
                {
                    language = NormalizeLanguage(argument["--language=".Length..]);
                    continue;
                }

                if (normalizedArgument == "--language")
                {
                    if (
                        index + 1 < tokens.Length &&
                        !tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        language = NormalizeLanguage(tokens[++index]);
                    }

                    continue;
                }

                switch (normalizedArgument)
                {
                    case "--no-elevation-warning":
                        suppressElevationWarning = true;
                        break;
                    case "--absolute-paths":
                        useRelativePath = false;
                        break;
                    case "--retain-script":
                        retainScriptFile = true;
                        break;
                    case "--hide-success-dialog":
                        hideSuccessfulOperationDialog = true;
                        break;
                    default:
                        break;
                }
            }
        }

        return new StartupOptions(
            suppressElevationWarning,
            useRelativePath,
            retainScriptFile,
            hideSuccessfulOperationDialog,
            language);
    }

    private static string? NormalizeLanguage(string value)
    {
        string candidate = value.Trim().Trim('"');
        return SupportedLanguages.TryGetValue(candidate, out string? language)
            ? language
            : null;
    }
}
