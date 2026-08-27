namespace SymlinkCreator.Tests;

[TestClass]
public sealed class SymlinkScriptProgressTests(TestContext testContext)
{
    [TestMethod]
    public void ParseReportsFailedEntryAndRemovesInternalLines()
    {
        (string standardError, SymlinkScriptProgress progress) = SymlinkScriptProgressParser.Parse(
            7,
            string.Join(
                Environment.NewLine,
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}1",
                $"{SymlinkScriptProgressParser.EntrySuccessPrefix}1",
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}2",
                "mklink failed",
                string.Empty),
            expectedEntryCount: 2);

        Assert.AreEqual(1, progress.FailedEntryIndex);
        Assert.AreEqual(1, progress.SuccessfulEntryCount);
        Assert.AreEqual("mklink failed", standardError.Trim());
    }

    [TestMethod]
    public void ParseIgnoresMalformedDuplicateAndOutOfRangeMarkers()
    {
        (string standardError, SymlinkScriptProgress progress) = SymlinkScriptProgressParser.Parse(
            7,
            string.Join(
                Environment.NewLine,
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}1",
                $"{SymlinkScriptProgressParser.EntrySuccessPrefix}1",
                $"{SymlinkScriptProgressParser.EntrySuccessPrefix}1",
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}3",
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}not-a-number",
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}2",
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}2",
                $"{SymlinkScriptProgressParser.EntrySuccessPrefix}3",
                "mklink failed",
                string.Empty),
            expectedEntryCount: 2);

        Assert.AreEqual(1, progress.FailedEntryIndex);
        Assert.AreEqual(1, progress.SuccessfulEntryCount);
        Assert.Contains($"{SymlinkScriptProgressParser.EntrySuccessPrefix}1", standardError);
        Assert.Contains($"{SymlinkScriptProgressParser.EntryAttemptPrefix}3", standardError);
        Assert.Contains($"{SymlinkScriptProgressParser.EntryAttemptPrefix}not-a-number", standardError);
        Assert.Contains($"{SymlinkScriptProgressParser.EntryAttemptPrefix}2", standardError);
        Assert.Contains($"{SymlinkScriptProgressParser.EntrySuccessPrefix}3", standardError);
        Assert.Contains("mklink failed", standardError);
    }

    [TestMethod]
    public async Task BatchProgressMarkerCommandsAreCapturedForAttribution()
    {
        using var temporary = TemporaryDirectory.Create();
        string scriptPath = temporary.CreateFile(
            "progress.cmd",
            string.Join(
                Environment.NewLine,
                "@echo off",
                $">&2 echo {SymlinkScriptProgressParser.EntryAttemptPrefix}1",
                $">&2 echo {SymlinkScriptProgressParser.EntrySuccessPrefix}1",
                $">&2 echo {SymlinkScriptProgressParser.EntryAttemptPrefix}2",
                "echo mklink failed 1>&2",
                "exit /b 7",
                string.Empty));

        ProcessTestResult processResult = await ProcessTestHelper.RunBatchAsync(
            scriptPath,
            testContext.CancellationToken);
        (string standardError, SymlinkScriptProgress progress) = SymlinkScriptProgressParser.Parse(
            processResult.ExitCode,
            processResult.StandardError,
            expectedEntryCount: 2);

        Assert.AreEqual(7, processResult.ExitCode);
        Assert.AreEqual(1, progress.FailedEntryIndex);
        Assert.AreEqual(1, progress.SuccessfulEntryCount);
        Assert.AreEqual("mklink failed", standardError.Trim());
    }
}
