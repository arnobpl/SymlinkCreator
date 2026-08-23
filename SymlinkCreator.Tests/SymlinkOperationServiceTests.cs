namespace SymlinkCreator.Tests;

[TestClass]
public sealed class SymlinkOperationServiceTests
{
    [TestMethod]
    public void ExecuteDeletesGeneratedScriptWhenRetentionIsDisabled()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(0, string.Empty));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkOperationResult result = service.Execute(new SymlinkRequest(
            new[] { sourcePath },
            destinationDirectory,
            RetainScriptFile: false));

        Assert.IsNull(result.RetainedScriptPath);
        Assert.HasCount(1, runner.Paths);
        Assert.IsFalse(File.Exists(runner.Paths.Single()));
        Assert.Contains("mklink", runner.Scripts.Single());
    }

    [TestMethod]
    public void ExecuteRetainsGeneratedScriptWhenRequested()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(0, string.Empty));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkOperationResult result = service.Execute(new SymlinkRequest(
            new[] { sourcePath },
            destinationDirectory,
            RetainScriptFile: true));

        Assert.AreEqual(runner.Paths.Single(), result.RetainedScriptPath);
        Assert.IsTrue(File.Exists(result.RetainedScriptPath));
        Assert.StartsWith(Path.Combine(temporary.Root, "desktop"), result.RetainedScriptPath);
        ScriptWorkspace.DeleteIfExists(result.RetainedScriptPath);
    }

    [TestMethod]
    public void ExecuteMapsCancellationAndCleansUpNonRetainedScript()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(-1, "cancelled", WasCancelled: true));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkExecutionException exception = Assert.ThrowsExactly<SymlinkExecutionException>(() =>
            service.Execute(new SymlinkRequest(new[] { sourcePath }, destinationDirectory)));

        Assert.IsTrue(exception.WasCancelled);
        Assert.Contains("canceled", exception.Message);
        Assert.IsFalse(File.Exists(runner.Paths.Single()));
    }

    [TestMethod]
    public void ExecuteMapsNonZeroExitAndIncludesStandardError()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt");
        string destinationDirectory = temporary.CreateDirectory("destination");
        var runner = new FakeProcessRunner(new ProcessExecutionResult(7, "target already exists"));
        SymlinkOperationService service = CreateService(temporary, runner);

        SymlinkExecutionException exception = Assert.ThrowsExactly<SymlinkExecutionException>(() =>
            service.Execute(new SymlinkRequest(new[] { sourcePath }, destinationDirectory)));

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

        public ProcessExecutionResult Run(string scriptPath)
        {
            Paths.Add(scriptPath);
            Scripts.Add(File.ReadAllText(scriptPath));
            return _result;
        }
    }
}
