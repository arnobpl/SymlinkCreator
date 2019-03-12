using System.Diagnostics;
using System.IO;

namespace SymlinkCreator.core
{
    class ScriptExecutor : StreamWriter
    {
        #region members

        private readonly string _fileName;

        #endregion


        #region constructor

        public ScriptExecutor(string fileName) : base(fileName)
        {
            this._fileName = fileName;
        }

        #endregion


        #region methods

        /// <summary>
        /// Executes the script as admin. It blocks the control flow while executing.
        /// </summary>
        public void ExecuteAsAdmin()
        {
            Close();

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = _fileName,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process process = Process.Start(processStartInfo);
            if (process == null) return;

            process.WaitForExit();
            process.Close();
        }

        #endregion
    }
}