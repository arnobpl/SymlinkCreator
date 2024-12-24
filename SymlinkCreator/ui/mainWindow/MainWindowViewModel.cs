using SymlinkCreator.Properties;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SymlinkCreator.ui.mainWindow
{
    internal class MainWindowViewModel : INotifyPropertyChanged
    {
        #region properties

        public ObservableCollection<string> FileOrFolderList { get; set; } = new ObservableCollection<string>();

        private string _destinationPath;
        public string DestinationPath
        {
            get { return _destinationPath; }
            set
            {
                _destinationPath = value;
                OnPropertyChanged();
            }
        }

        public bool ShouldUseRelativePath { get; set; } = true;

        public bool ShouldRetainScriptFile { get; set; } = false;

        public bool HideSuccessfulOperationDialog { get; set; } = false;

        #endregion


        #region helper methods

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}