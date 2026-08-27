namespace SymlinkCreator.Tests;

[TestClass]
public sealed class SymlinkOperationServiceTests
{
    [TestMethod]
    public void TryDeleteTemporaryFileLeavesLockedFileWithoutThrowing()
    {
        using var temporary = TemporaryDirectory.Create();
        string filePath = temporary.CreateFile("locked.tmp");
        using var lockStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        Assert.IsFalse(ScriptWorkspace.TryDeleteTemporaryFile(filePath));

        Assert.IsTrue(File.Exists(filePath));
    }

    [TestMethod]
    public async Task ExecuteDeletesGeneratedScriptWhenRetentionIsDisabled()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(0, string.Empty));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkOperationResult result = await service.ExecuteAsync(new SymlinkRequest(
            new[] { sourcePath },
            destinationDirectory,
            RetainScriptFile: false),
            CancellationToken.None);

        Assert.IsNull(result.RetainedScriptPath);
        Assert.HasCount(1, runner.Paths);
        Assert.IsFalse(File.Exists(runner.Paths.Single()));
        Assert.Contains("mklink", runner.Scripts.Single());
    }

    [TestMethod]
    public async Task ExecuteRetainsGeneratedScriptWhenRequested()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(0, string.Empty));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkOperationResult result = await service.ExecuteAsync(new SymlinkRequest(
            new[] { sourcePath },
            destinationDirectory,
            RetainScriptFile: true),
            CancellationToken.None);

        Assert.AreEqual(runner.Paths.Single(), result.RetainedScriptPath);
        Assert.IsTrue(File.Exists(result.RetainedScriptPath));
        Assert.StartsWith(Path.Combine(temporary.Root, "desktop"), result.RetainedScriptPath);
        ScriptWorkspace.TryDeleteTemporaryFile(result.RetainedScriptPath);
    }

    [TestMethod]
    public async Task ExecuteForwardsCancellationTokenToProcessRunner()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(0, string.Empty));
        SymlinkOperationService service = CreateService(temporary, runner);
        using var cancellation = new CancellationTokenSource();

        await service.ExecuteAsync(
            new SymlinkRequest(new[] { sourcePath }, destinationDirectory),
            cancellation.Token);

        Assert.AreEqual(cancellation.Token, runner.LastCancellationToken);
    }

    [TestMethod]
    public async Task ExecuteMapsCancellationProgressAndCleansUpNonRetainedScript()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(
            -1,
            string.Join(
                Environment.NewLine,
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}1",
                $"{SymlinkScriptProgressParser.EntrySuccessPrefix}1"),
            WasCancelled: true));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkExecutionException exception = await Assert.ThrowsExactlyAsync<SymlinkExecutionException>(() =>
            service.ExecuteAsync(
                new SymlinkRequest(new[] { sourcePath }, destinationDirectory),
                CancellationToken.None));

        Assert.IsTrue(exception.WasCancelled);
        Assert.AreEqual(1, exception.Progress.SuccessfulEntryCount);
        Assert.AreEqual(1, exception.TotalEntryCount);
        Assert.AreEqual(string.Empty, exception.StandardError);
        Assert.IsFalse(File.Exists(runner.Paths.Single()));
    }

    [TestMethod]
    public async Task ExecuteParsesFailedLinkAndPartialSuccess()
    {
        using var temporary = TemporaryDirectory.Create();
        string firstSourcePath = temporary.CreateFile("first.txt");
        string secondSourcePath = temporary.CreateFile("second.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(
            7,
            string.Join(
                Environment.NewLine,
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}1",
                $"{SymlinkScriptProgressParser.EntrySuccessPrefix}1",
                $"{SymlinkScriptProgressParser.EntryAttemptPrefix}2",
                "target already exists")));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkExecutionException exception = await Assert.ThrowsExactlyAsync<SymlinkExecutionException>(() =>
            service.ExecuteAsync(
                new SymlinkRequest(new[] { firstSourcePath, secondSourcePath }, destinationDirectory),
                CancellationToken.None));

        Assert.AreEqual(Path.Combine(destinationDirectory, "second.txt"), exception.FailedLinkPath);
        Assert.AreEqual(1, exception.Progress.SuccessfulEntryCount);
        Assert.AreEqual(2, exception.TotalEntryCount);
        Assert.Contains("target already exists", exception.StandardError);
    }

    [TestMethod]
    public async Task ExecuteMapsNonZeroExitAndIncludesStandardError()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(7, "target already exists"));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkExecutionException exception = await Assert.ThrowsExactlyAsync<SymlinkExecutionException>(() =>
            service.ExecuteAsync(
                new SymlinkRequest(new[] { sourcePath }, destinationDirectory),
                CancellationToken.None));

        Assert.AreEqual(7, exception.ExitCode);
        Assert.Contains("target already exists", exception.Message);
        Assert.IsFalse(File.Exists(runner.Paths.Single()));
    }

    private static SymlinkOperationService CreateService(
        TemporaryDirectory temporary,
        FakeProcessRunner runner)
    {
        var workspace = new ScriptWorkspace(
            Path.Combine(temporary.Root, "scripts"),
            Path.Combine(temporary.Root, "desktop"));
        return new SymlinkOperationService(
            new SymlinkPlanner(),
            new SymlinkScriptGenerator(),
            workspace,
            runner);
    }

    private sealed class FakeProcessRunner(ProcessExecutionResult result) : IPrivilegedProcessRunner
    {
        private readonly ProcessExecutionResult _result = result;

        public List<string> Paths { get; } = [];

        public List<string> Scripts { get; } = [];

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ProcessExecutionResult> RunAsync(
            string scriptPath,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            Paths.Add(scriptPath);
            Scripts.Add(File.ReadAllText(scriptPath));
            return Task.FromResult(_result);
        }
    }
}
