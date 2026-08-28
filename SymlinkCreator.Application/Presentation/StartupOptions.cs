namespace SymlinkCreator.Application.Presentation;

public enum ThemePreference
{
    Light,
    Dark
}

public sealed record StartupOptions(
    bool SuppressElevationWarning = false,
    bool UseRelativePath = true,
    bool RetainScriptFile = false,
    bool HideSuccessfulOperationDialog = false,
    string? Language = null,
    ThemePreference? Theme = null)
{
    private const string LanguageOption = "--language";
    private const string ThemeOption = "--theme";

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
        return ParseTokens(
            string.IsNullOrWhiteSpace(arguments)
                ? []
                : arguments.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    public static StartupOptions ParseCommandLineArguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return ParseTokens(arguments);
    }

    private static StartupOptions ParseTokens(IEnumerable<string> tokens)
    {
        bool suppressElevationWarning = false;
        bool useRelativePath = true;
        bool retainScriptFile = false;
        bool hideSuccessfulOperationDialog = false;
        string? language = null;
        ThemePreference? theme = null;

        string[] arguments = [.. tokens];
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            string normalizedArgument = argument.ToLowerInvariant();

            if (TryReadValueOption(arguments, ref index, LanguageOption, out string? languageValue))
            {
                if (languageValue is not null)
                {
                    language = NormalizeLanguage(languageValue);
                }
            }
            else if (TryReadValueOption(arguments, ref index, ThemeOption, out string? themeValue))
            {
                if (themeValue is not null)
                {
                    theme = NormalizeTheme(themeValue);
                }
            }
            else
            {
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
            language,
            theme);
    }

    private static bool TryReadValueOption(
        string[] arguments,
        ref int index,
        string optionName,
        out string? value)
    {
        string argument = arguments[index];
        string optionWithEquals = string.Concat(optionName, "=");

        if (argument.StartsWith(optionWithEquals, StringComparison.OrdinalIgnoreCase))
        {
            value = argument[optionWithEquals.Length..];
            return true;
        }

        if (!string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return false;
        }

        if (index + 1 < arguments.Length &&
            !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = arguments[++index];
        }
        else
        {
            value = null;
        }

        return true;
    }

    private static string? NormalizeLanguage(string value)
    {
        string candidate = value.Trim().Trim('"');
        return SupportedLanguages.TryGetValue(candidate, out string? language)
            ? language
            : null;
    }

    private static ThemePreference? NormalizeTheme(string value)
    {
        return value.Trim().Trim('"').ToLowerInvariant() switch
        {
            "light" => ThemePreference.Light,
            "dark" => ThemePreference.Dark,
            _ => null
        };
    }
}
