namespace SymlinkCreator.Core;

public static class ApplicationMetadata
{
    public const string FileName = "SymlinkCreator";
    public const string Company = "Arnob Paul";
    public static string Version => typeof(ApplicationMetadata).Assembly.GetName().Version?.ToString(3)
        ?? throw new InvalidOperationException("The application assembly version is not available.");
    public const string Website = "https://github.com/arnobpl/SymlinkCreator";

    public static Uri WebsiteUri => new(Website, UriKind.Absolute);
}
