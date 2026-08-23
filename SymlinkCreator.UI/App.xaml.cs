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
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly MrtCoreStringResourceService _resources;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

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
        var startupOptions = StartupOptions.Parse(args.Arguments);
        _resources.SetLanguage(startupOptions.Language);
        _mainWindowViewModel.ApplyStartupOptions(startupOptions);
        _window = new MainWindow(
            _mainWindowViewModel,
            _resources,
            startupOptions);
        _window.Activate();
    }
}
