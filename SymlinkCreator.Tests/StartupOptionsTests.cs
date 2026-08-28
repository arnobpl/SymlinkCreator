namespace SymlinkCreator.Tests;

[TestClass]
public sealed class StartupOptionsTests
{
    [TestMethod]
    public void DefaultsMatchInteractiveApplicationBehavior()
    {
        var options = StartupOptions.Parse(null);

        Assert.IsFalse(options.SuppressElevationWarning);
        Assert.IsTrue(options.UseRelativePath);
        Assert.IsFalse(options.RetainScriptFile);
        Assert.IsFalse(options.HideSuccessfulOperationDialog);
        Assert.IsNull(options.Language);
        Assert.IsNull(options.Theme);
    }

    [TestMethod]
    public void ParseRecognizesAllSupportedFlags()
    {
        var options = StartupOptions.Parse(
            "--no-elevation-warning --absolute-paths --retain-script --hide-success-dialog --language ja-JP --theme dark");

        Assert.IsTrue(options.SuppressElevationWarning);
        Assert.IsFalse(options.UseRelativePath);
        Assert.IsTrue(options.RetainScriptFile);
        Assert.IsTrue(options.HideSuccessfulOperationDialog);
        Assert.AreEqual("ja-JP", options.Language);
        Assert.AreEqual(ThemePreference.Dark, options.Theme);
    }

    [TestMethod]
    public void ParseCommandLineArgumentsRecognizesProcessArguments()
    {
        var options = StartupOptions.ParseCommandLineArguments(
        [
            "--no-elevation-warning",
            "--absolute-paths",
            "--retain-script",
            "--hide-success-dialog",
            "--language",
            "bn-BD",
            "--theme",
            "light"
        ]);

        Assert.IsTrue(options.SuppressElevationWarning);
        Assert.IsFalse(options.UseRelativePath);
        Assert.IsTrue(options.RetainScriptFile);
        Assert.IsTrue(options.HideSuccessfulOperationDialog);
        Assert.AreEqual("bn-BD", options.Language);
        Assert.AreEqual(ThemePreference.Light, options.Theme);
    }

    [TestMethod]
    public void ParseIsCaseInsensitiveAndIgnoresUnknownArguments()
    {
        var options = StartupOptions.Parse(
            "  --NO-ELEVATION-WARNING\t--Absolute-Paths --LANGUAGE=FR --unknown-option ");

        Assert.IsTrue(options.SuppressElevationWarning);
        Assert.IsFalse(options.UseRelativePath);
        Assert.IsFalse(options.RetainScriptFile);
        Assert.IsFalse(options.HideSuccessfulOperationDialog);
        Assert.AreEqual("fr", options.Language);
        Assert.IsNull(options.Theme);
    }

    [TestMethod]
    public void ParseIgnoresUnsupportedLanguage()
    {
        var options = StartupOptions.Parse("--language xx-YY");

        Assert.IsNull(options.Language);
    }

    [TestMethod]
    public void ParseIgnoresUnsupportedTheme()
    {
        var options = StartupOptions.Parse("--theme sepia");

        Assert.IsNull(options.Theme);
    }

    [TestMethod]
    public void ParseDoesNotConsumeFollowingOptionAsValue()
    {
        var options = StartupOptions.Parse("--theme --language");

        Assert.IsNull(options.Theme);
        Assert.IsNull(options.Language);
    }

    [TestMethod]
    public void ParsePreservesValueWhenLaterOptionHasNoValue()
    {
        var options = StartupOptions.Parse("--theme dark --theme --language fr --language");

        Assert.AreEqual(ThemePreference.Dark, options.Theme);
        Assert.AreEqual("fr", options.Language);
    }

    [TestMethod]
    public void ParseSupportsEqualsThemeSyntax()
    {
        var options = StartupOptions.Parse("--theme=LIGHT");

        Assert.AreEqual(ThemePreference.Light, options.Theme);
    }

}
