using SymlinkCreator.Core;

namespace SymlinkCreator.Tests;

[TestClass]
public sealed class SymlinkScriptGeneratorTests
{
    [TestMethod]
    public void GenerateQuotesPathsAndAddsDirectorySwitch()
    {
        var plan = new SymlinkPlan(
            "C:\\Destination Folder",
            new[]
            {
                new SymlinkEntry(
                    "C:\\Source Folder\\file.txt",
                    "C:\\Destination Folder\\file.txt",
                    "file.txt",
                    "..\\Source Folder\\file.txt",
                    IsDirectory: false),
                new SymlinkEntry(
                    "C:\\Source Folder\\folder",
                    "C:\\Destination Folder\\folder",
                    "folder",
                    "..\\Source Folder\\folder",
                    IsDirectory: true)
            });

        string script = new SymlinkScriptGenerator().Generate(plan);

        Assert.Contains("setlocal DisableDelayedExpansion", script);
        Assert.Contains("cd /d \"C:\\Destination Folder\"", script);
        Assert.Contains("mklink \"file.txt\" \"..\\Source Folder\\file.txt\"", script);
        Assert.Contains("mklink /d \"folder\" \"..\\Source Folder\\folder\"", script);
        Assert.AreEqual(2, CountOccurrences(script, "if errorlevel 1 exit /b %errorlevel%"));
    }

    [TestMethod]
    public void GenerateDoublesPercentSignsForBatchFiles()
    {
        var plan = new SymlinkPlan(
            "C:\\Destination",
            new[]
            {
                new SymlinkEntry(
                    "C:\\Source\\100%.txt",
                    "C:\\Destination\\100%.txt",
                    "100%.txt",
                    "..\\Source\\100%.txt",
                    IsDirectory: false)
            });

        string script = new SymlinkScriptGenerator().Generate(plan);

        Assert.Contains("\"100%%.txt\"", script);
    }

    private static int CountOccurrences(string value, string searchValue)
    {
        return value.Split(searchValue, StringSplitOptions.None).Length - 1;
    }
}
