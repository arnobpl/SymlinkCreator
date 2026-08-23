using SymlinkCreator.Core;

namespace SymlinkCreator.Tests;

[TestClass]
public sealed class PathInputTests
{
    [TestMethod]
    public void ParseLinesExcludesEmptyLinesAndSanitizesEachPath()
    {
        IReadOnlyList<string> result = PathInput.ParseLines("\"C:\\One\"\r\n\r\n C:\\Two \n");

        string[] expected = ["C:\\One", "C:\\Two"];
        Assert.AreSequenceEqual(expected, result.ToArray());
    }

    [TestMethod]
    public void ExistenceChecksDistinguishEntriesFromResolvedFilesAndDirectories()
    {
        using var temporary = TemporaryDirectory.Create();
        string filePath = temporary.CreateFile("source.txt", "content");
        string directoryPath = Directory.CreateDirectory(Path.Combine(temporary.Root, "folder")).FullName;

        Assert.IsTrue(PathInput.EntryExists(filePath));
        Assert.IsTrue(PathInput.EntryExists(directoryPath));
        Assert.IsTrue(PathInput.FileOrDirectoryExists(filePath));
        Assert.IsTrue(PathInput.FileOrDirectoryExists(directoryPath));

        string arbitraryText = Path.Combine(temporary.Root, "the effort you put into it.");
        Assert.IsFalse(PathInput.EntryExists(arbitraryText));
        Assert.IsFalse(PathInput.FileOrDirectoryExists(arbitraryText));

        string? missingPath = PathInput.FindFirstMissingFileOrDirectory(
            [filePath, arbitraryText, directoryPath]);
        Assert.AreEqual(arbitraryText, missingPath);
        Assert.IsNull(PathInput.FindFirstMissingFileOrDirectory([filePath, directoryPath]));
    }
}
