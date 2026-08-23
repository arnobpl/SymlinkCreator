using SymlinkCreator.Core;

namespace SymlinkCreator.Tests;

[TestClass]
public sealed class MainWindowViewModelTests
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
        Assert.IsFalse(viewModel.CanCreateSymlinks);
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
    public void TryCreateSymlinksReportsMissingSourcesAndDestinationWithoutExecuting()
    {
        MainWindowViewModel viewModel = CreateViewModel(out FakeOperationService operationService);

        Assert.IsFalse(viewModel.TryCreateSymlinks());
        Assert.AreEqual("No files or folders were selected.", viewModel.ErrorMessage);
        Assert.IsEmpty(operationService.Requests);

        viewModel.AddSourcePaths(SingleSourcePath);
        Assert.IsFalse(viewModel.TryCreateSymlinks());
        Assert.AreEqual("Destination path is empty.", viewModel.ErrorMessage);
        Assert.IsEmpty(operationService.Requests);
    }

    [TestMethod]
    public void TryCreateSymlinksOrchestratesOperationAndMapsSuccess()
    {
        MainWindowViewModel viewModel = CreateViewModel(out FakeOperationService operationService);
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";
        viewModel.UseRelativePath = false;
        viewModel.RetainScriptFile = true;

        Assert.IsTrue(viewModel.TryCreateSymlinks());
        SymlinkRequest request = operationService.Requests.Single();
        Assert.AreSequenceEqual(SingleSourcePath, request.SourcePaths.ToArray());
        Assert.AreEqual("destination", request.DestinationDirectory);
        Assert.IsFalse(request.UseRelativePath);
        Assert.IsTrue(request.RetainScriptFile);
        Assert.AreEqual("Execution completed.", viewModel.SuccessMessage);
        Assert.IsNull(viewModel.ErrorMessage);
    }

    [TestMethod]
    public void TryCreateSymlinksHideSuccessOptionSuppressesSuccessMessage()
    {
        MainWindowViewModel viewModel = CreateViewModel(out _);
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";
        viewModel.HideSuccessfulOperationDialog = true;

        Assert.IsTrue(viewModel.TryCreateSymlinks());
        Assert.IsNull(viewModel.SuccessMessage);
    }

    [TestMethod]
    public void TryCreateSymlinksMapsCancellationAndFailureMessages()
    {
        MainWindowViewModel canceledViewModel = CreateViewModel(
            out _,
            new SymlinkExecutionException("ignored", -1, wasCancelled: true));
        canceledViewModel.AddSourcePaths(SingleSourcePath);
        canceledViewModel.DestinationPath = "destination";

        Assert.IsFalse(canceledViewModel.TryCreateSymlinks());
        Assert.AreEqual("The elevation request was canceled.", canceledViewModel.ErrorMessage);

        MainWindowViewModel failedViewModel = CreateViewModel(
            out _,
            new SymlinkExecutionException("target already exists", 7, wasCancelled: false));
        failedViewModel.AddSourcePaths(SingleSourcePath);
        failedViewModel.DestinationPath = "destination";

        Assert.IsFalse(failedViewModel.TryCreateSymlinks());
        string? errorMessage = failedViewModel.ErrorMessage;
        Assert.IsNotNull(errorMessage);
        Assert.Contains("Symlink creation failed.", errorMessage);
        Assert.Contains("target already exists", errorMessage);
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
    public void TryCreateSymlinksLocalizesCoreValidationErrors(
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

        Assert.IsFalse(viewModel.TryCreateSymlinks());
        Assert.AreEqual(expectedMessage, viewModel.ErrorMessage);
    }

    [TestMethod]
    public void TryCreateSymlinksReportsExpectedSystemFailures()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            out _,
            new IOException("The script directory is unavailable."));
        viewModel.AddSourcePaths(SingleSourcePath);
        viewModel.DestinationPath = "destination";

        Assert.IsFalse(viewModel.TryCreateSymlinks());
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

        public SymlinkOperationResult Execute(SymlinkRequest request)
        {
            Requests.Add(request);
            return _exception is not null
                ? throw _exception
                : new SymlinkOperationResult(
                new SymlinkPlan(request.DestinationDirectory, Array.Empty<SymlinkEntry>()),
                null,
                new ProcessExecutionResult(0, string.Empty));
        }
    }
}
