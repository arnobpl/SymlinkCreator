namespace SymlinkCreator.Tests;

[TestClass]
public sealed class MainWindowViewModelTests(TestContext testContext)
{
    private static readonly string[] SingleSourcePath = ["source.txt"];

    [TestMethod]
    public void DefaultsMatchExpectedApplicationBehavior()
    {
        MainWindowViewModel viewModel = CreateViewModel(out _);

        Assert.IsEmpty(viewModel.SourcePaths);
        Assert.AreEqual(string.Empty, viewModel.DestinationPath);
        Assert.IsTrue(viewModel.UseRelativePath);
        Assert.IsFalse(viewModel.RetainScriptFile);
        Assert.IsFalse(viewModel.HideSuccessfulOperationDialog);
        Assert.IsFalse(viewModel.IsCreatingSymlinks);
        Assert.IsFalse(viewModel.CanCreateSymlinks);
        Assert.IsTrue(viewModel.CanEditRequest);
    }

    [TestMethod]
    public void ApplyStartupOptionsSetsLaunchPreferences()
    {
        MainWindowViewModel viewModel = CreateViewModel(out _);

        viewModel.ApplyStartupOptions(new StartupOptions(
            SuppressElevationWarning: true,
            UseRelativePath: false,
            RetainScriptFile: true,
            HideSuccessfulOperationDialog: true));

        Assert.IsFalse(viewModel.UseRelativePath);
        Assert.IsTrue(viewModel.RetainScriptFile);
        Assert.IsTrue(viewModel.HideSuccessfulOperationDialog);
    }

    [TestMethod]
    public void ListOperationsSanitizeDeduplicateRemoveAndClearPaths()
    {
        MainWindowViewModel viewModel = CreateViewModel(out _);

        string[] paths = [" \"one.txt\" ", "one.txt", "TWO.txt"];
        viewModel.AddSourcePaths(paths);
        viewModel.SetDestinationPath(" \"destination\" ");

        Assert.HasCount(2, viewModel.SourcePaths);
        Assert.AreEqual("destination", viewModel.DestinationPath);
        Assert.IsTrue(viewModel.CanCreateSymlinks);

        viewModel.RemoveSourcePath("one.txt");
        string[] expected = ["TWO.txt"];
        Assert.AreSequenceEqual(expected, viewModel.SourcePaths.ToArray());

        viewModel.ClearSourcePaths();
        Assert.IsEmpty(viewModel.SourcePaths);
        Assert.IsFalse(viewModel.CanCreateSymlinks);
    }

    [TestMethod]
    public async Task TryCreateSymlinksReportsMissingSourcesAndDestinationWithoutExecuting()
    {
        MainWindowViewModel viewModel = CreateViewModel(out FakeOperationService operationService);

        Assert.IsFalse(await viewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        Assert.AreEqual("No files or folders were selected.", viewModel.ErrorMessage);
        Assert.IsEmpty(operationService.Requests);

        viewModel.AddSourcePaths(SingleSourcePath);
        Assert.IsFalse(await viewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        Assert.AreEqual("Destination path is empty.", viewModel.ErrorMessage);
        Assert.IsEmpty(operationService.Requests);
    }

    [TestMethod]
    public async Task TryCreateSymlinksOrchestratesOperationAndMapsSuccess()
    {
        MainWindowViewModel viewModel = CreateViewModel(out FakeOperationService operationService);
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";
        viewModel.UseRelativePath = false;
        viewModel.RetainScriptFile = true;

        Assert.IsTrue(await viewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        SymlinkRequest request = operationService.Requests.Single();
        Assert.AreSequenceEqual(SingleSourcePath, request.SourcePaths.ToArray());
        Assert.AreEqual("destination", request.DestinationDirectory);
        Assert.IsFalse(request.UseRelativePath);
        Assert.IsTrue(request.RetainScriptFile);
        Assert.AreEqual("Execution completed.", viewModel.SuccessMessage);
        Assert.IsNull(viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task TryCreateSymlinksPreventsConcurrentOperations()
    {
        MainWindowViewModel viewModel = CreateViewModel(out FakeOperationService operationService);
        operationService.Gate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";

        Task<bool> operation = viewModel.TryCreateSymlinksAsync(testContext.CancellationToken);
        await operationService.Started.Task.WaitAsync(testContext.CancellationToken);

        Assert.IsTrue(viewModel.IsCreatingSymlinks);
        Assert.IsFalse(viewModel.CanCreateSymlinks);
        Assert.IsFalse(viewModel.CanEditRequest);
        Assert.IsFalse(await viewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        Assert.HasCount(1, operationService.Requests);

        operationService.Gate.SetResult(true);
        Assert.IsTrue(await operation);
        Assert.IsFalse(viewModel.IsCreatingSymlinks);
        Assert.IsTrue(viewModel.CanCreateSymlinks);
        Assert.IsTrue(viewModel.CanEditRequest);
    }

    [TestMethod]
    public async Task TryCreateSymlinksHideSuccessOptionSuppressesSuccessMessage()
    {
        MainWindowViewModel viewModel = CreateViewModel(out _);
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";
        viewModel.HideSuccessfulOperationDialog = true;

        Assert.IsTrue(await viewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        Assert.IsNull(viewModel.SuccessMessage);
    }

    [TestMethod]
    public async Task TryCreateSymlinksMapsCancellationAndFailureMessages()
    {
        MainWindowViewModel canceledViewModel = CreateViewModel(
            out _,
            new SymlinkExecutionException(ProcessExecutionResult.CancellationMessage, -1, wasCancelled: true));
        canceledViewModel.AddSourcePaths(SingleSourcePath);
        canceledViewModel.DestinationPath = "destination";

        Assert.IsFalse(await canceledViewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        Assert.AreEqual("The elevation request was canceled.", canceledViewModel.ErrorMessage);

        MainWindowViewModel diagnosticViewModel = CreateViewModel(
            out _,
            new SymlinkExecutionException(
                "The operation completed naturally.",
                -1,
                wasCancelled: true,
                progress: new SymlinkScriptProgress(SuccessfulEntryCount: 1),
                totalEntryCount: 2));
        diagnosticViewModel.AddSourcePaths(SingleSourcePath);
        diagnosticViewModel.DestinationPath = "destination";

        Assert.IsFalse(await diagnosticViewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        string diagnosticErrorMessage = diagnosticViewModel.ErrorMessage
            ?? throw new AssertFailedException("Expected a cancellation diagnostic.");
        Assert.Contains("The elevation request was canceled.", diagnosticErrorMessage);
        Assert.Contains("Links created before the operation stopped: 1 of 2.", diagnosticErrorMessage);

        MainWindowViewModel failedViewModel = CreateViewModel(
            out _,
            new SymlinkExecutionException(
                "target already exists",
                7,
                wasCancelled: false,
                failedLinkPath: "destination\\source.txt",
                progress: new SymlinkScriptProgress(SuccessfulEntryCount: 1),
                totalEntryCount: 2));
        failedViewModel.AddSourcePaths(SingleSourcePath);
        failedViewModel.DestinationPath = "destination";

        Assert.IsFalse(await failedViewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        string? errorMessage = failedViewModel.ErrorMessage;
        Assert.IsNotNull(errorMessage);
        Assert.Contains("Symlink creation failed.", errorMessage);
        Assert.Contains("The operation failed while creating 'destination\\source.txt'.", errorMessage);
        Assert.Contains("Links created before the operation stopped: 1 of 2.", errorMessage);
        Assert.Contains("The symlink script exited with code 7.", errorMessage);
        Assert.Contains("target already exists", errorMessage);
    }

    [TestMethod]
    public async Task TryCreateSymlinksMapsCanceledOperationToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        MainWindowViewModel viewModel = CreateViewModel(
            out _,
            new OperationCanceledException(cancellation.Token));
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";

        Assert.IsFalse(await viewModel.TryCreateSymlinksAsync(cancellation.Token));
        Assert.AreEqual("The elevation request was canceled.", viewModel.ErrorMessage);
    }

    [TestMethod]
    [DataRow(SymlinkValidationError.NoSources, "No files or folders were selected.", null)]
    [DataRow(SymlinkValidationError.DestinationEmpty, "Destination path is empty.", null)]
    [DataRow(SymlinkValidationError.DestinationNotFound, "Destination path does not exist: C:\\Missing", "C:\\Missing")]
    [DataRow(SymlinkValidationError.DestinationContainsInvalidCharacters, "Destination path contains invalid characters.", null)]
    [DataRow(SymlinkValidationError.DestinationInvalid, "Destination path is invalid: invalid", "invalid")]
    [DataRow(SymlinkValidationError.SourceEmpty, "Source path is empty.", null)]
    [DataRow(SymlinkValidationError.SourceNotFound, "Source path does not exist: C:\\Missing.txt", "C:\\Missing.txt")]
    [DataRow(SymlinkValidationError.SourceContainsInvalidCharacters, "Source path contains invalid characters.", null)]
    [DataRow(SymlinkValidationError.SourceInvalid, "Source path is invalid: invalid", "invalid")]
    [DataRow(SymlinkValidationError.DuplicateLinkName, "Multiple source paths would create the duplicate link name 'same.txt'.", "same.txt")]
    [DataRow(SymlinkValidationError.DestinationEntryExists, "The destination already contains 'source.txt'.", "source.txt")]
    [DataRow(SymlinkValidationError.InvalidLinkName, "The source path cannot be used as a link name: C:\\", "C:\\")]
    [DataRow(SymlinkValidationError.EmptyPlan, "The symlink plan contains no entries.", null)]
    [DataRow(SymlinkValidationError.GeneratedPathContainsInvalidCharacters, "A generated script path contains invalid characters.", null)]
    public async Task TryCreateSymlinksLocalizesValidationErrors(
        SymlinkValidationError error,
        string expectedMessage,
        string? messageArgument)
    {
        MainWindowViewModel viewModel = CreateViewModel(
            out _,
            new SymlinkValidationException(
                error,
                "This diagnostic must not be displayed.",
                messageArgument is null ? [] : [messageArgument]));
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";

        Assert.IsFalse(await viewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        Assert.AreEqual(expectedMessage, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task TryCreateSymlinksReportsExpectedSystemFailures()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            out _,
            new IOException("The script directory is unavailable."));
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";

        Assert.IsFalse(await viewModel.TryCreateSymlinksAsync(testContext.CancellationToken));
        Assert.AreEqual(
            "Symlink creation could not be started. The script directory is unavailable.",
            viewModel.ErrorMessage);
    }

    private static MainWindowViewModel CreateViewModel(
        out FakeOperationService operationService,
        Exception? exception = null)
    {
        operationService = new FakeOperationService(exception);
        return new MainWindowViewModel(operationService, new TestResourceService());
    }

    private sealed class TestResourceService : IStringResourceService
    {
        private static readonly Dictionary<string, string> Values = new()
        {
            ["NoSourcesError"] = "No files or folders were selected.",
            ["DestinationEmptyError"] = "Destination path is empty.",
            ["DestinationNotFoundFormat"] = "Destination path does not exist: {0}",
            ["DestinationInvalidCharactersError"] = "Destination path contains invalid characters.",
            ["DestinationInvalidFormat"] = "Destination path is invalid: {0}",
            ["SourceEmptyError"] = "Source path is empty.",
            ["SourceNotFoundFormat"] = "Source path does not exist: {0}",
            ["SourceInvalidCharactersError"] = "Source path contains invalid characters.",
            ["SourceInvalidFormat"] = "Source path is invalid: {0}",
            ["DuplicateLinkNameFormat"] = "Multiple source paths would create the duplicate link name '{0}'.",
            ["DestinationEntryExistsFormat"] = "The destination already contains '{0}'.",
            ["InvalidLinkNameFormat"] = "The source path cannot be used as a link name: {0}",
            ["EmptyPlanError"] = "The symlink plan contains no entries.",
            ["GeneratedPathInvalidCharactersError"] = "A generated script path contains invalid characters.",
            ["ExecutionCompleted"] = "Execution completed.",
            ["ExecutionFailed"] = "Symlink creation failed.",
            ["ExecutionExitCodeFormat"] = "The symlink script exited with code {0}.",
            ["ExecutionFailedAtLinkFormat"] = "The operation failed while creating '{0}'.",
            ["ExecutionPartialSuccessFormat"] = "Links created before the operation stopped: {0} of {1}.",
            ["UnexpectedExecutionErrorFormat"] = "Symlink creation could not be started. {0}",
            ["ElevationCanceled"] = "The elevation request was canceled."
        };

        public string GetString(string key)
        {
            return Values[key];
        }
    }

    private sealed class FakeOperationService(Exception? exception) : ISymlinkOperationService
    {
        private readonly Exception? _exception = exception;

        public List<SymlinkRequest> Requests { get; } = [];

        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<SymlinkOperationResult> ExecuteAsync(
            SymlinkRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Started.TrySetResult(true);
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return _exception is not null
                ? throw _exception
                : new SymlinkOperationResult(
                new SymlinkPlan(request.DestinationDirectory, Array.Empty<SymlinkEntry>()),
                null,
                new ProcessExecutionResult(0, string.Empty));
        }
    }
}
