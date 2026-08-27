using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SymlinkCreator.Application.Core;

namespace SymlinkCreator.Application.Presentation;

public sealed class MainWindowViewModel(
    ISymlinkOperationService operationService,
    IStringResourceService resources) : INotifyPropertyChanged
{
    private readonly ISymlinkOperationService _operationService =
        operationService ?? throw new ArgumentNullException(nameof(operationService));
    private readonly IStringResourceService _resources =
        resources ?? throw new ArgumentNullException(nameof(resources));

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> SourcePaths { get; } = [];

    public string DestinationPath
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    public bool UseRelativePath
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool RetainScriptFile
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool HideSuccessfulOperationDialog
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsCreatingSymlinks
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string? ErrorMessage
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string? SuccessMessage
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool CanCreateSymlinks =>
        !IsCreatingSymlinks &&
        SourcePaths.Count > 0 &&
        !string.IsNullOrWhiteSpace(DestinationPath);

    public bool CanEditRequest => !IsCreatingSymlinks;

    public void ApplyStartupOptions(StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        UseRelativePath = options.UseRelativePath;
        RetainScriptFile = options.RetainScriptFile;
        HideSuccessfulOperationDialog = options.HideSuccessfulOperationDialog;
    }

    public void AddSourcePaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths.Select(PathInput.Sanitize).Where(static path => path.Length > 0))
        {
            if (!SourcePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                SourcePaths.Add(path);
            }
        }

        OnCollectionChanged();
    }

    public void RemoveSourcePath(string? path)
    {
        if (path is not null)
        {
            _ = SourcePaths.Remove(path);
            OnCollectionChanged();
        }
    }

    public void ClearSourcePaths()
    {
        SourcePaths.Clear();
        OnCollectionChanged();
    }

    public void SetDestinationPath(string? path)
    {
        DestinationPath = PathInput.Sanitize(path);
    }

    public async Task<bool> TryCreateSymlinksAsync(CancellationToken cancellationToken = default)
    {
        if (IsCreatingSymlinks)
        {
            return false;
        }

        ErrorMessage = null;
        SuccessMessage = null;

        if (SourcePaths.Count == 0)
        {
            ErrorMessage = _resources.GetString("NoSourcesError");
            return false;
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            ErrorMessage = _resources.GetString("DestinationEmptyError");
            return false;
        }

        IsCreatingSymlinks = true;
        try
        {
            await _operationService.ExecuteAsync(new SymlinkRequest(
                [.. SourcePaths],
                DestinationPath,
                UseRelativePath,
                RetainScriptFile),
                cancellationToken);
            SuccessMessage = HideSuccessfulOperationDialog
                ? null
                : _resources.GetString("ExecutionCompleted");
            return true;
        }
        catch (SymlinkExecutionException exception)
        {
            ErrorMessage = exception.WasCancelled
                ? CreateCancellationMessage(exception)
                : CreateFailureMessage(exception);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = _resources.GetString("ElevationCanceled");
            return false;
        }
        catch (SymlinkValidationException exception)
        {
            ErrorMessage = GetValidationMessage(exception);
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            ErrorMessage = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _resources.GetString("UnexpectedExecutionErrorFormat"),
                exception.Message);
            return false;
        }
        finally
        {
            IsCreatingSymlinks = false;
        }
    }

    private string GetValidationMessage(SymlinkValidationException exception)
    {
        string resourceKey = exception.Error switch
        {
            SymlinkValidationError.NoSources => "NoSourcesError",
            SymlinkValidationError.DestinationEmpty => "DestinationEmptyError",
            SymlinkValidationError.DestinationNotFound => "DestinationNotFoundFormat",
            SymlinkValidationError.DestinationContainsInvalidCharacters => "DestinationInvalidCharactersError",
            SymlinkValidationError.DestinationInvalid => "DestinationInvalidFormat",
            SymlinkValidationError.SourceEmpty => "SourceEmptyError",
            SymlinkValidationError.SourceNotFound => "SourceNotFoundFormat",
            SymlinkValidationError.SourceContainsInvalidCharacters => "SourceInvalidCharactersError",
            SymlinkValidationError.SourceInvalid => "SourceInvalidFormat",
            SymlinkValidationError.DuplicateLinkName => "DuplicateLinkNameFormat",
            SymlinkValidationError.DestinationEntryExists => "DestinationEntryExistsFormat",
            SymlinkValidationError.InvalidLinkName => "InvalidLinkNameFormat",
            SymlinkValidationError.EmptyPlan => "EmptyPlanError",
            SymlinkValidationError.GeneratedPathContainsInvalidCharacters => "GeneratedPathInvalidCharactersError",
            _ => throw new ArgumentOutOfRangeException(nameof(exception))
        };

        string format = _resources.GetString(resourceKey);
        return exception.MessageArguments.Count == 0
            ? format
            : string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                format,
                [.. exception.MessageArguments]);
    }

    private void OnCollectionChanged()
    {
        OnPropertyChanged(nameof(CanCreateSymlinks));
    }

    private string CreateCancellationMessage(SymlinkExecutionException exception)
    {
        List<string> details = [_resources.GetString("ElevationCanceled")];
        AddExecutionProgress(details, exception);
        return string.Join(Environment.NewLine, details);
    }

    private string CreateFailureMessage(SymlinkExecutionException exception)
    {
        List<string> details = [_resources.GetString("ExecutionFailed")];
        if (exception.FailedLinkPath is not null)
        {
            details.Add(string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _resources.GetString("ExecutionFailedAtLinkFormat"),
                exception.FailedLinkPath));
        }

        AddExecutionProgress(details, exception);
        details.Add(string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            _resources.GetString("ExecutionExitCodeFormat"),
            exception.ExitCode));
        string standardError = exception.StandardError.Trim();
        if (standardError.Length > 0)
        {
            details.Add(standardError);
        }
        return string.Join(Environment.NewLine, details);
    }

    private void AddExecutionProgress(
        List<string> details,
        SymlinkExecutionException exception)
    {
        if (exception.Progress.SuccessfulEntryCount is int successfulEntryCount &&
            exception.TotalEntryCount is int totalEntryCount)
        {
            details.Add(string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _resources.GetString("ExecutionPartialSuccessFormat"),
                successfulEntryCount,
                totalEntryCount));
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(DestinationPath) or nameof(IsCreatingSymlinks))
        {
            OnPropertyChanged(nameof(CanCreateSymlinks));
            if (propertyName == nameof(IsCreatingSymlinks))
            {
                OnPropertyChanged(nameof(CanEditRequest));
            }
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
