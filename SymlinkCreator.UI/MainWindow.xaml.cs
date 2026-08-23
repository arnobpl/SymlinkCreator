using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using SymlinkCreator.Application.Core;
using SymlinkCreator.Application.Platform;
using SymlinkCreator.Application.Presentation;
using System.Globalization;
using System.Security.Principal;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;

namespace SymlinkCreator;

public sealed partial class MainWindow : Window
{
    private readonly bool _suppressElevationWarning;
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
        ConfigureWindow();
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
        AboutButton.Click += AboutButton_Click;
        ApplyLocalizedStrings();
        ApplyToolTips();
        MainContent.Loaded += MainContent_Loaded;
    }

    public MainWindowViewModel ViewModel { get; }

    public IStringResourceService StringResources { get; }

    private void ConfigureWindow()
    {
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

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
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

        if (ViewModel.TryCreateSymlinks())
        {
            if (!ViewModel.HideSuccessfulOperationDialog)
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
        AddFilesButton.Content = StringResources.GetString("AddFilesButton.Content");
        AddFoldersButton.Content = StringResources.GetString("AddFoldersButton.Content");
        RemoveSelectedButton.Content = StringResources.GetString("RemoveSelectedButton.Content");
        ClearListButton.Content = StringResources.GetString("ClearListButton.Content");
        DestinationPathHeader.Text = StringResources.GetString("DestinationPathHeader.Text");
        BrowseButton.Content = StringResources.GetString("BrowseButton.Content");
        RelativePathCheckBox.Content = StringResources.GetString("RelativePathCheckBox.Content");
        RetainScriptCheckBox.Content = StringResources.GetString("RetainScriptCheckBox.Content");
        HideSuccessCheckBox.Content = StringResources.GetString("HideSuccessCheckBox.Content");
        CreateSymlinksButtonLabel.Text = StringResources.GetString("CreateSymlinksButtonLabel.Text");
        AboutButton.Content = StringResources.GetString("AboutButton.Content");
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

        _ = await dialog.ShowAsync();
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
