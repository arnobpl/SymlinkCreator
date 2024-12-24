using Microsoft.WindowsAPICodePack.Dialogs;
using SymlinkCreator.core;
using SymlinkCreator.ui.aboutWindow;
using SymlinkCreator.ui.utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;


namespace SymlinkCreator.ui.mainWindow
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region constructor

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;

            if (IsRunningAsAdmin())
            {
                MessageBox.Show(
                    $"Running {this.Title} as an administrator may disable drag-n-drop functionality. " +
                    "Only symlink creation requires administrative rights. " +
                    "Please restart the application without administrative privileges to enable drag-n-drop functionality.",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        #endregion


        #region fields

        private string _previouslySelectedDestinationFolderPath = "";

        #endregion


        #region window event handles

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.DataContext = new MainWindowViewModel();
        }

        protected override void OnSourceInitialized(EventArgs eventArgs)
        {
            WindowMaximizeButton.DisableMaximizeButton(this);
            this.CreateSymlinksButtonImage.Source = NativeAdminShieldIcon.GetNativeShieldIcon();
            base.OnSourceInitialized(eventArgs);
        }

        #endregion


        #region control event handles

        private void AddFilesButton_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog fileDialog = new OpenFileDialog
            {
                Multiselect = true
            };

            if (fileDialog.ShowDialog() == true)
            {
                AddToSourceFileOrFolderList(fileDialog.FileNames);
            }
        }

        private void AddFoldersButton_OnClick(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog folderBrowserDialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Multiselect = true
            };

            if (folderBrowserDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                AddToSourceFileOrFolderList(folderBrowserDialog.FileNames);
            }
        }

        private void DestinationPathBrowseButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            CommonOpenFileDialog folderBrowserDialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                InitialDirectory = _previouslySelectedDestinationFolderPath
            };

            if (folderBrowserDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                mainWindowViewModel.DestinationPath = folderBrowserDialog.FileName;
                _previouslySelectedDestinationFolderPath = folderBrowserDialog.FileName;
            }
        }

        private void RemoveSelectedButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            List<string> selectedFileOrFolderList = SourceFileOrFolderListView.SelectedItems.Cast<string>().ToList();
            foreach (string selectedItem in selectedFileOrFolderList)
            {
                mainWindowViewModel.FileOrFolderList.Remove(selectedItem);
            }
        }

        private void ClearListButton_OnClick(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel mainWindowViewModel = this.DataContext as MainWindowViewModel;

            mainWindowViewModel?.FileOrFolderList.Clear();
        }

        private void SourceFileOrFolderListView_OnDrop(object sender, DragEventArgs e)
        {
            string[] droppedFileOrFolderList = GetDroppedFileOrFolderList(e);
            if (droppedFileOrFolderList != null)
            {
                AddToSourceFileOrFolderList(droppedFileOrFolderList);
            }
        }

        private void DestinationPathTextBox_OnDrop(object sender, DragEventArgs e)
        {
            string[] pathList = GetDroppedFileOrFolderList(e);
            if (pathList != null)
            {
                string droppedDestinationPath = pathList[0];
                AssignDestinationPath(droppedDestinationPath);
                e.Handled = true;
            }
        }

        private void DestinationPathTextBox_OnPreviewDragOver(object sender, DragEventArgs e)
        {
            string[] pathList = GetDroppedFileOrFolderList(e);
            if (pathList != null)
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void CreateSymlinksButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            if (mainWindowViewModel.FileOrFolderList.Count == 0)
            {
                MessageBox.Show(this, "No files or folders to create symlinks for.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(mainWindowViewModel.DestinationPath))
            {
                MessageBox.Show(this, "Destination path is empty.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            mainWindowViewModel.DestinationPath = SanitizePath(mainWindowViewModel.DestinationPath);

            SymlinkAgent symlinkAgent = new SymlinkAgent(
                mainWindowViewModel.FileOrFolderList,
                mainWindowViewModel.DestinationPath,
                mainWindowViewModel.ShouldUseRelativePath,
                mainWindowViewModel.ShouldRetainScriptFile);

            try
            {
                symlinkAgent.CreateSymlinks();
                if (!mainWindowViewModel.HideSuccessfulOperationDialog)
                {
                    MessageBox.Show(this, "Execution completed.", "Done!", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AboutButton_OnClick(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog();
        }

        #endregion


        #region helper methods

        private string[] GetDroppedFileOrFolderList(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                string droppedFileOrFolderList = (string)e.Data.GetData(DataFormats.Text);
                return GetFileOrFolderListFromString(droppedFileOrFolderList);
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                try
                {
                    return (string[])e.Data.GetData(DataFormats.FileDrop);
                }
                catch (COMException) // Handle long-path scenarios
                {
                    return LongPathAware.GetPathsFromShellIdListArray(e.Data).ToArray();
                }
            }

            return null;
        }

        private string[] GetFileOrFolderListFromString(string fileOrFolderListString)
        {
            return fileOrFolderListString
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => SanitizePath(path)) // Sanitize each individual path
                .Where(path => !string.IsNullOrWhiteSpace(path)) // Ensure no empty strings
                .ToArray();
        }

        private string SanitizePath(string path)
        {
            return path.Trim() // Trim any surrounding whitespace
                       .Trim('"'); // Remove surrounding quotation marks if present
        }

        private void AddToSourceFileOrFolderList(IEnumerable<string> fileOrFolderList)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            foreach (string fileOrFolder in fileOrFolderList)
            {
                if (!mainWindowViewModel.FileOrFolderList.Contains(fileOrFolder))
                {
                    mainWindowViewModel.FileOrFolderList.Add(fileOrFolder);
                }
            }
        }

        private void AssignDestinationPath(string destinationPath)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            if (Directory.Exists(destinationPath))
                mainWindowViewModel.DestinationPath = destinationPath;
        }

        private bool IsRunningAsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        #endregion
    }
}