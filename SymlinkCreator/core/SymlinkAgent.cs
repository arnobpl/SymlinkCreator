using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SymlinkCreator.core
{
    public class SymlinkAgent
    {
        #region members

        private readonly List<string> _sourceFileOrFolderList;
        private string _destinationPath;
        private readonly bool _shouldUseRelativePath;
        private readonly bool _shouldRetainScriptFile;

        private string[] _splittedDestinationPath;

        #endregion


        #region constructor

        public SymlinkAgent(IEnumerable<string> sourceFileOrFolderList, string destinationPath,
            bool shouldUseRelativePath = true, bool shouldRetainScriptFile = false)
        {
            this._sourceFileOrFolderList = sourceFileOrFolderList.ToList();
            this._destinationPath = destinationPath;
            this._shouldUseRelativePath = shouldUseRelativePath;
            this._shouldRetainScriptFile = shouldRetainScriptFile;
        }

        #endregion


        #region methods

        public void CreateSymlinks()
        {
            // check for destination path
            if (!Directory.Exists(_destinationPath))
            {
                throw new FileNotFoundException("Destination path does not exist", _destinationPath);
            }

            // remove the last '\' character from the path if exists
            if (_destinationPath[_destinationPath.Length - 1] == '\\')
                _destinationPath = _destinationPath.Substring(0, _destinationPath.Length - 1);

            _splittedDestinationPath = GetSplittedPath(_destinationPath);

            string scriptFileName = ApplicationConfiguration.ApplicationName + "_" +
                                    DateTime.Now.Ticks.ToString() + ".bat";
            ScriptExecutor scriptExecutor = new ScriptExecutor(scriptFileName);

            // set code page to UTF-8 to support unicode file paths
            scriptExecutor.WriteLine("chcp 65001 >NUL");

            // go to destination path
            scriptExecutor.WriteLine(_splittedDestinationPath[0]);
            scriptExecutor.WriteLine("cd \"" + _destinationPath + "\"");

            foreach (string sourceFilePath in _sourceFileOrFolderList)
            {
                string[] splittedSourceFilePath = GetSplittedPath(sourceFilePath);

                string commandLineTargetPath = sourceFilePath;
                if (_shouldUseRelativePath)
                {
                    // check if both root drives are same
                    if (splittedSourceFilePath.First() == _splittedDestinationPath.First())
                    {
                        commandLineTargetPath = GetRelativePath(_splittedDestinationPath, splittedSourceFilePath);
                    }
                }

                scriptExecutor.Write("mklink ");
                if (Directory.Exists(sourceFilePath))
                    scriptExecutor.Write("/d ");

                scriptExecutor.WriteLine("\"" + splittedSourceFilePath.Last() + "\" " +
                                         "\"" + commandLineTargetPath + "\"");
            }

            scriptExecutor.ExecuteAsAdmin();

            if (!_shouldRetainScriptFile)
                File.Delete(scriptFileName);
        }

        #endregion


        #region helper methods

        private string[] GetSplittedPath(string path)
        {
            return path.Split('\\');
        }

        private string GetRelativePath(string[] splittedCurrentPath, string[] splittedTargetPath)
        {
            List<string> splittedCurrentPathList = splittedCurrentPath.ToList();
            List<string> splittedTargetPathList = splittedTargetPath.ToList();

            while (splittedCurrentPathList.Any() && splittedTargetPathList.Any())
            {
                if (splittedCurrentPathList.First() == splittedTargetPathList.First())
                {
                    splittedCurrentPathList.RemoveAt(0);
                    splittedTargetPathList.RemoveAt(0);
                }
                else
                {
                    break;
                }
            }

            StringBuilder relativePathStringBuilder = new StringBuilder();

            for (int i = 0; i < splittedCurrentPathList.Count; i++)
            {
                relativePathStringBuilder.Append("..\\");
            }

            foreach (string splittedPath in splittedTargetPathList)
            {
                relativePathStringBuilder.Append(splittedPath);
                relativePathStringBuilder.Append('\\');
            }

            if (relativePathStringBuilder[relativePathStringBuilder.Length - 1] == '\\')
                relativePathStringBuilder.Length--;

            return relativePathStringBuilder.ToString();
        }

        private string GetRelativePath(string currentPath, string targetPath)
        {
            return GetRelativePath(GetSplittedPath(currentPath), GetSplittedPath(targetPath));
        }

        #endregion
    }
}