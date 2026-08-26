namespace SymlinkCreator.Tests;

[TestClass]
public sealed class ProcessExecutionTests(TestContext testContext)
{
    [TestMethod]
    public async Task WrapperRunsScriptRedirectsStandardErrorAndForwardsExitCode()
    {
        using var temporary = TemporaryDirectory.Create();
        string scriptPath = temporary.CreateFile(
            Path.Combine("Scripts & 100%!", "চালান^ script.cmd"),
            string.Join(
                Environment.NewLine,
                "@echo off",
                "echo wrapper-test-error 1>&2",
                "exit /b 7",
                string.Empty));
        string standardErrorPath = Path.Combine(temporary.Root, "Logs & 100%!", "stderr.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(standardErrorPath)!);
        string wrapperPath = temporary.CreateFile(
            Path.Combine("Scripts with spaces", "wrapper.cmd"),
            ElevatedScriptRunner.CreateWrapperScript(scriptPath, standardErrorPath));

        ProcessTestResult processResult = await ProcessTestHelper.RunBatchAsync(
            wrapperPath,
            testContext.CancellationToken);

        Assert.AreEqual(
            7,
            processResult.ExitCode,
            processResult.StandardOutput + processResult.StandardError);
        Assert.AreEqual("wrapper-test-error", File.ReadAllText(standardErrorPath).Trim());
    }

    [TestMethod]
    public async Task ElevatedRunnerReturnsCancellationAndCleansTemporaryFilesWhenAlreadyCanceled()
    {
        using var temporary = TemporaryDirectory.Create();
        var workspace = new ScriptWorkspace(Path.Combine(temporary.Root, "workspace"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        ProcessExecutionResult result = await new ElevatedScriptRunner(workspace).RunAsync(
            "script.cmd",
            cancellation.Token);

        Assert.AreEqual(-1, result.ExitCode);
        Assert.IsTrue(result.WasCancelled);
        Assert.Contains("canceled", result.StandardError);
        Assert.IsFalse(Directory.Exists(workspace.RootDirectory));
    }

    [TestMethod]
    public async Task RunBatchAsyncStopsTheProcessWhenCancellationIsRequested()
    {
        using var temporary = TemporaryDirectory.Create();
        string markerPath = Path.Combine(temporary.Root, "finished সম্পন্ন.txt");
        string scriptPath = temporary.CreateFile(
            "long-running.cmd",
            string.Join(
                Environment.NewLine,
                "@echo off",
                "ping.exe 127.0.0.1 -n 4 >NUL",
                $"echo completed>\"{markerPath}\"",
                string.Empty));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ProcessTestHelper.RunBatchAsync(scriptPath, cancellation.Token));

        await Task.Delay(TimeSpan.FromSeconds(4), testContext.CancellationToken);
        Assert.IsFalse(File.Exists(markerPath));
    }

    [TestMethod]
    public async Task ElevatedRunnerExecutesScriptAndCleansTemporaryFilesWhenEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SYMLINKCREATOR_RUN_ELEVATED_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive(
                "The real elevated-runner test is disabled. Set SYMLINKCREATOR_RUN_ELEVATED_TESTS=true in a suitable Windows test environment.");
        }

        using var temporary = TemporaryDirectory.Create();
        string scriptPath = temporary.CreateFile(
            Path.Combine("Scripts & 100%!", "চালান^ script.cmd"),
            string.Join(
                Environment.NewLine,
                "@echo off",
                "echo elevated-runner-test 1>&2",
                "exit /b 0",
                string.Empty));
        var workspace = new ScriptWorkspace(Path.Combine(temporary.Root, "workspace with spaces"));

        ProcessExecutionResult result = await new ElevatedScriptRunner(workspace).RunAsync(
            scriptPath,
            testContext.CancellationToken);

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.Contains("elevated-runner-test", result.StandardError);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(workspace.RootDirectory));
    }
}
