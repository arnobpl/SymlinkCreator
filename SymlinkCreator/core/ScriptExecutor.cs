using System;
using System.Diagnostics;
using System.IO;

namespace SymlinkCreator.core
{
    class ScriptExecutor : StreamWriter
    {
        #region members

        private readonly string _fileName;

        public int ExitCode { get; private set; } = -1;
        public string StandardError { get; private set; } = "";

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
            this.Close();

            // Wrapper script is required for both executing the actual script as admin and
            // redirecting the error output simultaneously.
            string wrapperScriptFileName = this._fileName + "_exe.cmd";
            string stderrFileName = this._fileName + "_err.txt";

            try
            {
                File.Create(stderrFileName).Dispose();
                CreateWrapperScript(wrapperScriptFileName, stderrFileName);
                ExecuteWrapperScript(wrapperScriptFileName, stderrFileName);
            }
            catch (Exception ex)
            {
                if (StandardError.Length > 0)
                {
                    StandardError += "\n";
                }
                StandardError += ex.ToString();
                if (ExitCode == 0)
                {
                    ExitCode = -1;
                }
            }
            finally
            {
                File.Delete(wrapperScriptFileName);
                File.Delete(stderrFileName);
            }
        }

        #endregion


        #region helper methods

        private void CreateWrapperScript(string wrapperScriptFileName, string stderrFileName)
        {
            StreamWriter wrapperScriptStreamWriter = new StreamWriter(wrapperScriptFileName);
            // redirect error output to file
            wrapperScriptStreamWriter.WriteLine(
                "\"" + Path.GetFullPath(this._fileName) + "\" 2> \"" + Path.GetFullPath(stderrFileName) + "\"");
            wrapperScriptStreamWriter.Close();
        }

        private void ExecuteWrapperScript(string wrapperScriptFileName, string stderrFileName)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = wrapperScriptFileName,
                UseShellExecute = true,
                Verb = "runas"
            };
            using (Process process = Process.Start(processStartInfo))
            {
                if (process != null)
                {
                    process.WaitForExit();
                    this.ExitCode = process.ExitCode;
                    this.StandardError = File.ReadAllText(stderrFileName);
                }
            }
        }

        #endregion
    }
}