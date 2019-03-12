using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SymlinkCreator.core
{
    internal static class ApplicationConfiguration
    {
        #region assembly properties

        private const int ApplicationVersionNumberCount = 3;

        private static string _applicationGuid;
        public static string ApplicationGuid
        {
            get
            {
                if (!string.IsNullOrEmpty(_applicationGuid))
                    return _applicationGuid;

                _applicationGuid = Assembly.GetExecutingAssembly().GetCustomAttribute<GuidAttribute>().Value;
                return _applicationGuid;
            }
        }

        private static string _applicationName;
        public static string ApplicationName
        {
            get
            {
                if (!string.IsNullOrEmpty(_applicationName))
                    return _applicationName;

                _applicationName = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyTitleAttribute>().Title;
                return _applicationName;
            }
        }

        private static string _applicationVersion;
        public static string ApplicationVersion
        {
            get
            {
                if (!string.IsNullOrEmpty(_applicationVersion))
                    return _applicationVersion;

                string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                string[] versionNumbers = version.Split('.');
                StringBuilder versionStringBuilder = new StringBuilder(version.Length);
                for (int i = 0; (i < versionNumbers.Length && i < ApplicationVersionNumberCount); i++)
                {
                    versionStringBuilder.Append(versionNumbers[i]);
                    versionStringBuilder.Append('.');
                }
                if (versionStringBuilder.Length != 0)
                    versionStringBuilder.Length--;

                _applicationVersion = versionStringBuilder.ToString();

                return _applicationVersion;
            }
        }

        private static string _applicationCompany;
        public static string ApplicationCompany
        {
            get
            {
                if (!string.IsNullOrEmpty(_applicationCompany))
                    return _applicationCompany;

                _applicationCompany =
                    Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCompanyAttribute>().Company;
                return _applicationCompany;
            }
        }

        #endregion


        #region web addresses

        public static string CompanyWebAddress => "https://github.com/arnobpl";

        #endregion
    }
}