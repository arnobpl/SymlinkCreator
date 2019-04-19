using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using SymlinkCreator.core;
using SymlinkCreator.ui.utility;
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
        }

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
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.Multiselect = true;
            bool? result = fileDialog.ShowDialog();

            if (result == true)
            {
                AddToSourceFileOrFolderList(fileDialog.FileNames);
            }
        }

        private void AddFolderButton_OnClick(object sender, RoutedEventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                DialogResult result = folderBrowserDialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    AddToSourceFileOrFolderList(folderBrowserDialog.SelectedPath);
                }
            }
        }

        private void DestinationPathBrowseButton_OnClick(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel mainWindowViewModel = this.DataContext as MainWindowViewModel;
            if (mainWindowViewModel == null) return;

            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                DialogResult result = folderBrowserDialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    mainWindowViewModel.DestinationPath = folderBrowserDialog.SelectedPath;
                }
            }
        }

        private void DeleteSelectedButton_OnClick(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel mainWindowViewModel = this.DataContext as MainWindowViewModel;
            if (mainWindowViewModel == null) return;

            List<string> selectedFileOrFolderList = SourceFileOrFolderListView.SelectedItems.Cast<string>().ToList();
            foreach (var selectedItem in selectedFileOrFolderList)
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
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFileOrFolderList = (string[]) e.Data.GetData(DataFormats.FileDrop);
                AddToSourceFileOrFolderList(droppedFileOrFolderList);
            }
        }

        private void DestinationPathTextBox_OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] pathList = (string[]) e.Data.GetData(DataFormats.FileDrop);
                if (pathList != null)
                {
                    string droppedDestinationPath = pathList[0];
                    AssignDestinationPath(droppedDestinationPath);
                }
            }
        }

        private void DestinationPathTextBox_OnPreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        private void CreateSymlinksButton_OnClick(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel mainWindowViewModel = this.DataContext as MainWindowViewModel;
            if (mainWindowViewModel == null) return;

            SymlinkAgent symlinkAgent = new SymlinkAgent(
                mainWindowViewModel.FileOrFolderList,
                mainWindowViewModel.DestinationPath,
                mainWindowViewModel.ShouldUseRelativePath,
                mainWindowViewModel.ShouldRetainScriptFile);

            symlinkAgent.CreateSymlinks();

            MessageBox.Show("Execution completed.", "Done!", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AboutButton_OnClick(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                $"{ApplicationConfiguration.ApplicationName} v{ApplicationConfiguration.ApplicationVersion}\n" +
                "Developed by Arnob Paul. Thank you for using this application! :)\n\n" +
                $"Do you want to visit the developer's website?\n{ApplicationConfiguration.CompanyWebAddress}",
                "About", MessageBoxButton.YesNo,
                MessageBoxImage.Asterisk);

            if (result == MessageBoxResult.Yes)
                Process.Start(ApplicationConfiguration.CompanyWebAddress);
        }

        #endregion


        #region helper methods

        private void AddToSourceFileOrFolderList(IEnumerable<string> fileOrFolderList)
        {
            MainWindowViewModel mainWindowViewModel = this.DataContext as MainWindowViewModel;
            if (mainWindowViewModel == null) return;

            foreach (string fileOrFolder in fileOrFolderList)
            {
                if (!mainWindowViewModel.FileOrFolderList.Contains(fileOrFolder))
                {
                    mainWindowViewModel.FileOrFolderList.Add(fileOrFolder);
                }
            }
        }

        private void AddToSourceFileOrFolderList(string fileOrFolderPath)
        {
            MainWindowViewModel mainWindowViewModel = this.DataContext as MainWindowViewModel;
            if (mainWindowViewModel == null) return;

            if (!mainWindowViewModel.FileOrFolderList.Contains(fileOrFolderPath))
            {
                mainWindowViewModel.FileOrFolderList.Add(fileOrFolderPath);
            }
        }

        private void AssignDestinationPath(string destinationPath)
        {
            MainWindowViewModel mainWindowViewModel = this.DataContext as MainWindowViewModel;
            if (mainWindowViewModel == null) return;

            if (Directory.Exists(destinationPath))
                mainWindowViewModel.DestinationPath = destinationPath;
        }

        #endregion
    }
}