namespace SymlinkCreator.Tests;

[TestClass]
public sealed class SymlinkPlannerTests
{
    [TestMethod]
    [DataRow("Abc\\Def\\Ghi", "Abc\\Def\\Qrs\\Test.mp3", "..\\Qrs\\Test.mp3")]
    [DataRow("Abc\\Def\\Ghi\\Jkl\\Mno", "Abc\\Def\\Qrs\\Test.mp3", "..\\..\\..\\Qrs\\Test.mp3")]
    [DataRow("Abc\\Def\\Ghi", "Abc\\Def\\Ghi\\Jkl\\Test.mp3", "Jkl\\Test.mp3")]
    [DataRow("Abc\\Def\\Ghi", "Abc\\Def\\Ghi\\Jkl\\Mno\\Test.mp3", "Jkl\\Mno\\Test.mp3")]
    public void CreatePlanComputesExpectedRelativePath(
        string destinationRelativePath,
        string sourceRelativePath,
        string expectedTargetPath)
    {
        using var temporary = TemporaryDirectory.Create();
        string destinationDirectory = temporary.CreateDirectory(destinationRelativePath);
        string sourcePath = temporary.CreateFile(sourceRelativePath);

        SymlinkEntry entry = new SymlinkPlanner()
            .CreatePlan([sourcePath], destinationDirectory)
            .Entries.Single();

        Assert.AreEqual(expectedTargetPath, entry.TargetPath);
    }

    [TestMethod]
    public void CreatePlanUsesRelativeFileTargetOnSameDrive()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourceDirectory = temporary.CreateDirectory("source");
        string destinationDirectory = temporary.CreateDirectory("destination");
        string sourcePath = Path.Combine(sourceDirectory, "song file.txt");
        File.WriteAllText(sourcePath, "content");

        SymlinkPlan plan = new SymlinkPlanner().CreatePlan(
            new[] { sourcePath },
            destinationDirectory,
            useRelativePath: true);

        SymlinkEntry entry = plan.Entries.Single();
        Assert.AreEqual(Path.GetFullPath(sourcePath), entry.SourcePath);
        Assert.AreEqual("..\\source\\song file.txt", entry.TargetPath);
        Assert.IsFalse(entry.IsDirectory);
    }

    [TestMethod]
    public void CreatePlanUsesDirectoryFlagAndRelativeDirectoryTarget()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateDirectory("source folder");
        string destinationDirectory = temporary.CreateDirectory("destination");

        SymlinkEntry entry = new SymlinkPlanner()
            .CreatePlan(new[] { sourcePath }, destinationDirectory)
            .Entries.Single();

        Assert.IsTrue(entry.IsDirectory);
        Assert.AreEqual("..\\source folder", entry.TargetPath);
    }

    [TestMethod]
    public void CreatePlanRejectsMissingDestination()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");

        SymlinkValidationException exception = Assert.ThrowsExactly<SymlinkValidationException>(() =>
            new SymlinkPlanner().CreatePlan(
                new[] { sourcePath },
                Path.Combine(temporary.Root, "missing")));

        Assert.AreEqual(SymlinkValidationError.DestinationNotFound, exception.Error);
    }

    [TestMethod]
    public void CreatePlanRejectsDuplicateLinkNames()
    {
        using var temporary = TemporaryDirectory.Create();
        string firstDirectory = temporary.CreateDirectory("first");
        string secondDirectory = temporary.CreateDirectory("second");
        string destinationDirectory = temporary.CreateDirectory("destination");
        string first = Path.Combine(firstDirectory, "same.txt");
        string second = Path.Combine(secondDirectory, "same.txt");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");

        SymlinkValidationException exception = Assert.ThrowsExactly<SymlinkValidationException>(() =>
            new SymlinkPlanner().CreatePlan(new[] { first, second }, destinationDirectory));

        Assert.AreEqual(SymlinkValidationError.DuplicateLinkName, exception.Error);
    }

    [TestMethod]
    public void CreatePlanRejectsExistingDestinationEntry()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        File.WriteAllText(Path.Combine(destinationDirectory, "source.txt"), "existing");

        SymlinkValidationException exception = Assert.ThrowsExactly<SymlinkValidationException>(() =>
            new SymlinkPlanner().CreatePlan(new[] { sourcePath }, destinationDirectory));

        Assert.AreEqual(SymlinkValidationError.DestinationEntryExists, exception.Error);
    }

    [TestMethod]
    public void CreatePlanPreservesSpecialCharactersInSourceName()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("special & (name) ^ % !.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");

        SymlinkEntry entry = new SymlinkPlanner()
            .CreatePlan(new[] { sourcePath }, destinationDirectory)
            .Entries.Single();

        Assert.AreEqual("special & (name) ^ % !.txt", entry.LinkName);
    }

    [TestMethod]
    public void CreatePlanSupportsUnicodeAndLongPathsWhenTheMachineAllowsThem()
    {
        using var temporary = TemporaryDirectory.Create();
        string longSourceDirectory = temporary.Root;

        for (int index = 0; index < 12; index++)
        {
            longSourceDirectory = Path.Combine(longSourceDirectory, $"長いディレクトリ_{index:00}_with spaces");
        }

        bool longPathFixtureCreated;
        try
        {
            _ = Directory.CreateDirectory(longSourceDirectory);
            longPathFixtureCreated = true;
        }
        catch (PathTooLongException)
        {
            longPathFixtureCreated = false;
        }

        if (!longPathFixtureCreated)
        {
            Assert.Inconclusive("The current environment does not permit the long-path test fixture.");
        }

        string sourcePath = Path.Combine(longSourceDirectory, "音楽 & 100%.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        File.WriteAllText(sourcePath, "unicode content");

        SymlinkEntry entry = new SymlinkPlanner()
            .CreatePlan(new[] { sourcePath }, destinationDirectory)
            .Entries.Single();

        Assert.AreEqual("音楽 & 100%.txt", entry.LinkName);
        Assert.IsTrue(entry.TargetPath.Contains("長いディレクトリ_11", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreatePlanUsesAbsoluteTargetWhenRootsDiffer()
    {
        string[] roots =
        [
            .. DriveInfo.GetDrives()
                .Where(static drive => drive.IsReady)
                .Select(static drive => drive.RootDirectory.FullName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
        ];

        if (roots.Length < 2)
        {
            Assert.Inconclusive("Two ready drive roots are required for this test.");
        }

        using var sourceRoot = TemporaryDirectory.Create(roots[0]);
        using var destinationRoot = TemporaryDirectory.Create(roots[1]);
        string sourcePath = sourceRoot.CreateFile("source.txt");
        string destinationDirectory = destinationRoot.CreateDirectory("destination");

        SymlinkEntry entry = new SymlinkPlanner()
            .CreatePlan(new[] { sourcePath }, destinationDirectory)
            .Entries.Single();

        Assert.AreEqual(Path.GetFullPath(sourcePath), entry.TargetPath);
    }
}
