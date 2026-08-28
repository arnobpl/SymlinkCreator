using Microsoft.UI.Xaml;
using SymlinkCreator.Application.Core;
using SymlinkCreator.Application.Presentation;
using SymlinkCreator.Localization;

namespace SymlinkCreator;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private readonly StartupOptions _startupOptions;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly MrtCoreStringResourceService _resources;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        _startupOptions = StartupOptions.ParseCommandLineArguments(
            Environment.GetCommandLineArgs().Skip(1));

        InitializeComponent();

        // Application.RequestedTheme can only be set during application startup.
        if (_startupOptions.Theme is ThemePreference theme)
        {
            RequestedTheme = theme == ThemePreference.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light;
        }

        var workspace = new ScriptWorkspace();
        var symlinkOperations = new SymlinkOperationService(
            new SymlinkPlanner(),
            new SymlinkScriptGenerator(),
            workspace,
            new ElevatedScriptRunner(workspace));
        string applicationDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        _resources = new MrtCoreStringResourceService(Path.Combine(applicationDirectory, "resources.pri"));
        _mainWindowViewModel = new MainWindowViewModel(symlinkOperations, _resources);
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = args;
        _resources.SetLanguage(_startupOptions.Language);
        _mainWindowViewModel.ApplyStartupOptions(_startupOptions);
        _window = new MainWindow(
            _mainWindowViewModel,
            _resources,
            _startupOptions);
        _window.Activate();
    }
}
