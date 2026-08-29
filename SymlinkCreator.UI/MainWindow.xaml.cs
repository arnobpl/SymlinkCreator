using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.Windows.Storage.Pickers;
using SymlinkCreator.Application.Core;
using SymlinkCreator.Application.Platform;
using SymlinkCreator.Application.Presentation;
using System.ComponentModel;
using System.Globalization;
using System.Security.Principal;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;

namespace SymlinkCreator;

public sealed partial class MainWindow : Window, IDisposable
{
    // The window owns the operation lifetime so closing it cancels work that could otherwise
    // finish after the UI has gone away.
    private readonly CancellationTokenSource _operationCancellation = new();
    private readonly bool _suppressElevationWarning;
    private bool _isClosed;
    private bool _operationCancellationDisposed;
    private string? _previouslySelectedDestinationFolderPath;

    public MainWindow(
        MainWindowViewModel viewModel,
        IStringResourceService resources,
        StartupOptions startupOptions)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        StringResources = resources ?? throw new ArgumentNullException(nameof(resources));
        ArgumentNullException.ThrowIfNull(startupOptions);
        _suppressElevationWarning = startupOptions.SuppressElevationWarning;

        InitializeComponent();
        ConfigureWindow(startupOptions.Theme);
        AddFilesButton.Click += AddFilesButton_Click;
        AddFoldersButton.Click += AddFoldersButton_Click;
        RemoveSelectedButton.Click += RemoveSelectedButton_Click;
        ClearListButton.Click += ClearListButton_Click;
        SourceListView.Drop += SourceListView_Drop;
        SourceListView.DragOver += DroppedPaths_DragOver;
        DestinationPathTextBox.Drop += DestinationPathTextBox_Drop;
        DestinationPathTextBox.DragOver += DroppedPaths_DragOver;
        BrowseButton.Click += BrowseButton_Click;
        CreateSymlinksButton.Click += CreateSymlinksButton_Click;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += MainWindow_Closed;
        AboutButton.Click += AboutButton_Click;
        ApplyLocalizedStrings();
        ApplyToolTips();
        UpdateCreateSymlinksIndicator();
        MainContent.Loaded += MainContent_Loaded;
    }

    public MainWindowViewModel ViewModel { get; }

    public IStringResourceService StringResources { get; }

    private void ConfigureWindow(ThemePreference? theme)
    {
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredTheme = theme switch
            {
                ThemePreference.Dark => TitleBarTheme.Dark,
                ThemePreference.Light => TitleBarTheme.Light,
                _ => TitleBarTheme.UseDefaultAppMode
            };
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        string windowTitle = string.Format(
            CultureInfo.CurrentCulture,
            StringResources.GetString("AppTitleBar.TitleFormat"),
            StringResources.GetString("ApplicationName"),
            ApplicationMetadata.Version);
        AppWindow.Title = windowTitle;
        AppTitleBarText.Text = windowTitle;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MainIcon.ico"));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = 1100;
            presenter.PreferredMinimumHeight = 650;
        }

        AppWindow.Resize(new SizeInt32(1200, 750));
    }

    private async void MainContent_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        MainContent.Loaded -= MainContent_Loaded;

        if (_suppressElevationWarning || !IsRunningAsAdministrator())
        {
            return;
        }

        string message = string.Format(
            CultureInfo.CurrentCulture,
            StringResources.GetString("ElevationWarning.Message"),
            StringResources.GetString("ApplicationName"));
        await ShowMessageAsync(StringResources.GetString("ElevationWarning.Title"), message);
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task<IReadOnlyList<string>> GetDroppedPathsAsync(DataPackageView dataView)
    {
        if (dataView.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await dataView.GetStorageItemsAsync();
            return [.. items.Select(static item => item.Path)];
        }

        return dataView.Contains(StandardDataFormats.Text)
            ? PathInput.ParseLines(await dataView.GetTextAsync())
            : Array.Empty<string>();
    }

    private static bool ContainsDroppedPaths(DataPackageView dataView)
    {
        return dataView.Contains(StandardDataFormats.StorageItems) ||
            dataView.Contains(StandardDataFormats.Text);
    }

    private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        string[] paths;
        try
        {
            FileOpenPicker picker = CreateFilePicker();
            IReadOnlyList<PickFileResult> files = await picker.PickMultipleFilesAsync();
            paths = [.. files.Select(static file => WindowsPath.ExpandShortNames(file.Path))];
        }
        catch (Exception exception)
        {
            await ShowFormattedErrorAsync("PickerErrorFormat", exception);
            return;
        }

        if (paths.Length != 0)
        {
            ViewModel.AddSourcePaths(paths);
        }
    }

    private async void AddFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        IReadOnlyList<string> folders;
        try
        {
            IReadOnlyList<PickFolderResult> results = await CreateFolderPicker().PickMultipleFoldersAsync();
            folders = [.. results.Select(static folder => WindowsPath.ExpandShortNames(folder.Path))];
        }
        catch (Exception exception)
        {
            await ShowFormattedErrorAsync("PickerErrorFormat", exception);
            return;
        }

        if (folders.Count != 0)
        {
            ViewModel.AddSourcePaths(folders);
        }
    }

    private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        foreach (string path in SourceListView.SelectedItems.OfType<string>().ToArray())
        {
            ViewModel.RemoveSourcePath(path);
        }
    }

    private void ClearListButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.ClearSourcePaths();
    }

    private async void SourceListView_Drop(object sender, DragEventArgs e)
    {
        _ = sender;

        try
        {
            IReadOnlyList<string> paths = await GetDroppedPathsAsync(e.DataView);
            string? missingPath = PathInput.FindFirstMissingFileOrDirectory(paths);
            if (missingPath is null)
            {
                ViewModel.AddSourcePaths(paths);
            }
            else
            {
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    StringResources.GetString("SourceNotFoundFormat"),
                    missingPath);
                await ShowMessageAsync(StringResources.GetString("ErrorDialog.Title"), message);
            }
        }
        catch (Exception exception)
        {
            await ShowFormattedErrorAsync("DroppedInputErrorFormat", exception);
        }

        e.Handled = true;
    }

    private void DroppedPaths_DragOver(object sender, DragEventArgs e)
    {
        _ = sender;
        e.AcceptedOperation = ContainsDroppedPaths(e.DataView)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private async void DestinationPathTextBox_Drop(object sender, DragEventArgs e)
    {
        _ = sender;

        try
        {
            IReadOnlyList<string> paths = await GetDroppedPathsAsync(e.DataView);
            if (paths.Count == 1 && Directory.Exists(paths[0]))
            {
                ViewModel.SetDestinationPath(paths[0]);
            }
            else
            {
                await ShowMessageAsync(
                    StringResources.GetString("ErrorDialog.Title"),
                    StringResources.GetString("DestinationDropError"));
            }
        }
        catch (Exception exception)
        {
            await ShowFormattedErrorAsync("DroppedInputErrorFormat", exception);
        }

        e.Handled = true;
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        string? folder;
        try
        {
            FolderPicker picker = CreateFolderPicker(_previouslySelectedDestinationFolderPath);
            PickFolderResult? result = await picker.PickSingleFolderAsync();
            folder = result is null ? null : WindowsPath.ExpandShortNames(result.Path);
        }
        catch (Exception exception)
        {
            await ShowFormattedErrorAsync("PickerErrorFormat", exception);
            return;
        }

        if (folder is not null)
        {
            ViewModel.SetDestinationPath(folder);
            _previouslySelectedDestinationFolderPath = folder;
        }
    }

    private async void CreateSymlinksButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (_isClosed)
        {
            return;
        }

        try
        {
            bool succeeded = await ViewModel.TryCreateSymlinksAsync(_operationCancellation.Token);
            // Cancellation and the awaited operation can race with Closed; never show a dialog
            // against a window that has already been disposed.
            if (_isClosed)
            {
                return;
            }

            if (succeeded)
            {
                if (ViewModel.SuccessMessage is not null)
                {
                    await ShowMessageAsync(
                        StringResources.GetString("SuccessDialog.Title"),
                        ViewModel.SuccessMessage ?? StringResources.GetString("ExecutionCompleted"));
                }

                return;
            }

            await ShowMessageAsync(
                StringResources.GetString("ErrorDialog.Title"),
                ViewModel.ErrorMessage ?? StringResources.GetString("ExecutionFailed"));
        }
        finally
        {
            DisposeOperationCancellationIfIdle();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs e)
    {
        _ = sender;
        _ = e;
        Dispose();
    }

    public void Dispose()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _operationCancellation.Cancel();
        DisposeOperationCancellationIfIdle();
    }

    private void DisposeOperationCancellationIfIdle()
    {
        // Signal cancellation first, then wait for the operation's finally block before
        // disposing the source so downstream token users cannot race with Dispose().
        if (_isClosed && !ViewModel.IsCreatingSymlinks && !_operationCancellationDisposed)
        {
            _operationCancellationDisposed = true;
            _operationCancellation.Dispose();
        }
    }

    private async void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = $"{StringResources.GetString("ApplicationName")} v{ApplicationMetadata.Version}"
        });
        content.Children.Add(new TextBlock
        {
            Text = string.Format(
                CultureInfo.CurrentCulture,
                StringResources.GetString("AboutDeveloperFormat"),
                ApplicationMetadata.Company)
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = StringResources.GetString("AboutWebsiteLabel"),
            NavigateUri = ApplicationMetadata.WebsiteUri,
            Padding = new Thickness(0)
        });

        await ShowDialogAsync(StringResources.GetString("AboutTitle"), content);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsCreatingSymlinks))
        {
            UpdateCreateSymlinksIndicator();
        }
    }

    private void UpdateCreateSymlinksIndicator()
    {
        bool isCreating = ViewModel.IsCreatingSymlinks;
        CreateSymlinksShieldIcon.Visibility = isCreating
            ? Visibility.Collapsed
            : Visibility.Visible;
        CreateSymlinksBusyIndicator.IsActive = isCreating;
    }

    private void ApplyToolTips()
    {
        ToolTipService.SetToolTip(SourceListView, StringResources.GetString("SourceListView.ToolTip"));
        ToolTipService.SetToolTip(AddFilesButton, StringResources.GetString("AddFilesButton.ToolTip"));
        ToolTipService.SetToolTip(AddFoldersButton, StringResources.GetString("AddFoldersButton.ToolTip"));
        ToolTipService.SetToolTip(RemoveSelectedButton, StringResources.GetString("RemoveSelectedButton.ToolTip"));
        ToolTipService.SetToolTip(ClearListButton, StringResources.GetString("ClearListButton.ToolTip"));
        ToolTipService.SetToolTip(DestinationPathTextBox, StringResources.GetString("DestinationPathTextBox.ToolTip"));
        ToolTipService.SetToolTip(BrowseButton, StringResources.GetString("BrowseButton.ToolTip"));
        ToolTipService.SetToolTip(RelativePathCheckBox, StringResources.GetString("RelativePathCheckBox.ToolTip"));
        ToolTipService.SetToolTip(RetainScriptCheckBox, StringResources.GetString("RetainScriptCheckBox.ToolTip"));
        ToolTipService.SetToolTip(HideSuccessCheckBox, StringResources.GetString("HideSuccessCheckBox.ToolTip"));
        ToolTipService.SetToolTip(CreateSymlinksButton, StringResources.GetString("CreateSymlinksButton.ToolTip"));
        ToolTipService.SetToolTip(AboutButton, StringResources.GetString("AboutButton.ToolTip"));
        AutomationProperties.SetName(CreateSymlinksButton, StringResources.GetString("CreateSymlinksButton.AccessibleName"));
        AutomationProperties.SetHelpText(CreateSymlinksButton, StringResources.GetString("CreateSymlinksButton.HelpText"));
    }

    private void ApplyLocalizedStrings()
    {
        SourceListHeader.Text = StringResources.GetString("SourceListHeader.Text");
        AddFilesButton.Text = StringResources.GetString("AddFilesButton.Content");
        AddFoldersButton.Text = StringResources.GetString("AddFoldersButton.Content");
        RemoveSelectedButton.Text = StringResources.GetString("RemoveSelectedButton.Content");
        ClearListButton.Text = StringResources.GetString("ClearListButton.Content");
        DestinationPathHeader.Text = StringResources.GetString("DestinationPathHeader.Text");
        BrowseButton.Text = StringResources.GetString("BrowseButton.Content");
        RelativePathCheckBox.Content = StringResources.GetString("RelativePathCheckBox.Content");
        RetainScriptCheckBox.Content = StringResources.GetString("RetainScriptCheckBox.Content");
        HideSuccessCheckBox.Content = StringResources.GetString("HideSuccessCheckBox.Content");
        CreateSymlinksButton.Text = StringResources.GetString("CreateSymlinksButtonLabel.Text");
        AboutButton.Text = StringResources.GetString("AboutButton.Content");
    }

    private FileOpenPicker CreateFilePicker()
    {
        return new FileOpenPicker(AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
    }

    private FolderPicker CreateFolderPicker(string? suggestedFolder = null)
    {
        var picker = new FolderPicker(AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            ViewMode = PickerViewMode.List
        };

        if (!string.IsNullOrWhiteSpace(suggestedFolder) && Directory.Exists(suggestedFolder))
        {
            picker.SuggestedFolder = suggestedFolder;
        }

        return picker;
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        await ShowDialogAsync(title, new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap });
    }

    private async Task ShowDialogAsync(string title, object content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = StringResources.GetString("DialogOk.Content"),
            XamlRoot = WindowRoot.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private Task ShowFormattedErrorAsync(string formatResourceKey, Exception exception)
    {
        string message = string.Format(
            CultureInfo.CurrentCulture,
            StringResources.GetString(formatResourceKey),
            exception.Message);
        return ShowMessageAsync(StringResources.GetString("ErrorDialog.Title"), message);
    }
}
