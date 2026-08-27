using System.Xml.Linq;

namespace SymlinkCreator.Tests;

[TestClass]
public sealed class LocalizationTests
{
    private static readonly string[] SupportedLanguages =
    [
        "bn-BD", "de", "en-US", "es", "fr", "ja-JP", "ko-KR", "pt-BR", "zh-CN"
    ];

    [TestMethod]
    [DataRow("bn-BD", "পরিচিতি")]
    [DataRow("de", "Info")]
    [DataRow("en-US", "About")]
    [DataRow("es", "Acerca de")]
    [DataRow("fr", "À propos")]
    [DataRow("ja-JP", "バージョン情報")]
    [DataRow("ko-KR", "정보")]
    [DataRow("pt-BR", "Sobre")]
    [DataRow("zh-CN", "关于")]
    public void LanguageOptionSelectsLocalizedResources(string language, string expectedAboutLabel)
    {
        var options = StartupOptions.Parse($"--language {language}");
        Assert.AreEqual(language, options.Language);

        Dictionary<string, string> resources = ReadResources(language);
        Assert.AreEqual(expectedAboutLabel, resources["AboutButton.Content"]);
    }

    [TestMethod]
    public void ResourceFilesContainTheSameNonEmptyRequiredKeys()
    {
        string[] requiredKeys =
        [
            "ApplicationName", "SourceListHeader.Text", "SourceListView.ToolTip",
            "AddFilesButton.Content", "AddFilesButton.ToolTip", "AddFoldersButton.Content",
            "AddFoldersButton.ToolTip", "RemoveSelectedButton.Content", "RemoveSelectedButton.ToolTip",
            "ClearListButton.Content", "ClearListButton.ToolTip", "DestinationPathHeader.Text",
            "DestinationPathTextBox.ToolTip", "BrowseButton.Content", "BrowseButton.ToolTip", "RelativePathCheckBox.Content",
            "RelativePathCheckBox.ToolTip", "RetainScriptCheckBox.Content", "RetainScriptCheckBox.ToolTip",
            "HideSuccessCheckBox.Content", "HideSuccessCheckBox.ToolTip", "CreateSymlinksButtonLabel.Text",
            "CreateSymlinksButton.ToolTip", "CreateSymlinksButton.AccessibleName",
            "CreateSymlinksButton.HelpText", "AboutButton.Content", "AboutButton.ToolTip", "NoSourcesError",
            "DestinationEmptyError", "DestinationNotFoundFormat", "DestinationInvalidCharactersError",
            "DestinationInvalidFormat", "SourceEmptyError", "SourceNotFoundFormat", "SourceInvalidCharactersError",
            "SourceInvalidFormat", "DuplicateLinkNameFormat", "DestinationEntryExistsFormat", "InvalidLinkNameFormat",
            "EmptyPlanError", "GeneratedPathInvalidCharactersError", "DestinationDropError", "DroppedInputErrorFormat",
            "PickerErrorFormat", "ExecutionCompleted", "ExecutionFailed", "ElevationCanceled", "AboutTitle",
            "ExecutionExitCodeFormat", "ExecutionFailedAtLinkFormat", "ExecutionPartialSuccessFormat",
            "UnexpectedExecutionErrorFormat", "AboutDeveloperFormat", "AboutWebsiteLabel", "AppTitleBar.TitleFormat", "ErrorDialog.Title",
            "ElevationWarning.Title", "ElevationWarning.Message", "SuccessDialog.Title", "DialogOk.Content"
        ];

        string[] resourceLanguages =
        [
            .. Directory
                .EnumerateDirectories(Path.Combine(AppContext.BaseDirectory, "Strings"))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
        ];
        Assert.AreSequenceEqual(SupportedLanguages.Order(StringComparer.Ordinal), resourceLanguages);

        Dictionary<string, string> defaultResources = ReadResources("en-US");
        Assert.AreSequenceEqual(requiredKeys.Order(), defaultResources.Keys.Order());

        foreach (string language in resourceLanguages)
        {
            Assert.AreEqual(language, StartupOptions.Parse($"--language {language}").Language);
            Dictionary<string, string> resources = ReadResources(language);
            Assert.AreSequenceEqual(defaultResources.Keys.Order(), resources.Keys.Order(), $"Resource keys differ for {language}.");
            foreach ((string key, string value) in resources)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"Resource key '{key}' is empty for {language}.");
            }
        }
    }

    [TestMethod]
    public void BuildProducesExternalPri()
    {
        string priPath = Path.Combine(AppContext.BaseDirectory, "resources.pri");
        Assert.IsTrue(File.Exists(priPath), $"Expected generated PRI at {priPath}");
        Assert.IsGreaterThan(0, new FileInfo(priPath).Length);
    }

    private static Dictionary<string, string> ReadResources(string language)
    {
        string resourcePath = Path.Combine(AppContext.BaseDirectory, "Strings", language, "Resources.resw");
        Assert.IsTrue(File.Exists(resourcePath), $"Expected localization source at {resourcePath}");

        var document = XDocument.Load(resourcePath);
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement data in document.Root?.Elements("data") ?? [])
        {
            string name = data.Attribute("name")?.Value
                ?? throw new InvalidDataException($"A resource in {resourcePath} has no name.");
            string value = data.Element("value")?.Value
                ?? throw new InvalidDataException($"Resource '{name}' in {resourcePath} has no value.");
            resources.Add(name, value);
        }

        return resources;
    }
}
