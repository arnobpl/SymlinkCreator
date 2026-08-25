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
    }

    [TestMethod]
    public void ParseRecognizesAllSupportedFlags()
    {
        var options = StartupOptions.Parse(
            "--no-elevation-warning --absolute-paths --retain-script --hide-success-dialog --language ja-JP");

        Assert.IsTrue(options.SuppressElevationWarning);
        Assert.IsFalse(options.UseRelativePath);
        Assert.IsTrue(options.RetainScriptFile);
        Assert.IsTrue(options.HideSuccessfulOperationDialog);
        Assert.AreEqual("ja-JP", options.Language);
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
            "bn-BD"
        ]);

        Assert.IsTrue(options.SuppressElevationWarning);
        Assert.IsFalse(options.UseRelativePath);
        Assert.IsTrue(options.RetainScriptFile);
        Assert.IsTrue(options.HideSuccessfulOperationDialog);
        Assert.AreEqual("bn-BD", options.Language);
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
    }

    [TestMethod]
    public void ParseIgnoresUnsupportedLanguage()
    {
        var options = StartupOptions.Parse("--language xx-YY");

        Assert.IsNull(options.Language);
    }

}
