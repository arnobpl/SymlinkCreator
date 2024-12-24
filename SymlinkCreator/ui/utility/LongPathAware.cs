using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace SymlinkCreator.ui.utility
{
    internal static class LongPathAware
    {
        #region constants

        private const string ShellIdListArrayName = "Shell IDList Array";
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        #endregion


        #region methods

        public static IEnumerable<string> GetPathsFromShellIdListArray(IDataObject data)
        {
            if (!data.GetDataPresent(ShellIdListArrayName)) yield break;

            using (MemoryStream ms = (MemoryStream)data.GetData(ShellIdListArrayName))
            {
                byte[] bytes = ms.ToArray();
                IntPtr p = Marshal.AllocHGlobal(bytes.Length);

                try
                {
                    Marshal.Copy(bytes, 0, p, bytes.Length);
                    uint cidl = (uint)Marshal.ReadInt32(p);
                    int offset = sizeof(uint);
                    IntPtr parentpidl = (IntPtr)((long)p + Marshal.ReadInt32(p, offset));

                    for (int i = 1; i <= cidl; ++i)
                    {
                        offset += sizeof(uint);
                        IntPtr relpidl = (IntPtr)((long)p + Marshal.ReadInt32(p, offset));
                        IntPtr abspidl = ILCombine(parentpidl, relpidl);

                        if (abspidl == IntPtr.Zero) continue;

                        if (SHGetNameFromIDList(abspidl, SIGDN_FILESYSPATH, out IntPtr pszName) == 0)
                        {
                            yield return Marshal.PtrToStringUni(pszName);
                            Marshal.FreeCoTaskMem(pszName);
                        }

                        ILFree(abspidl);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(p);
                }
            }
        }

        #endregion


        #region external methods

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

        [DllImport("shell32.dll")]
        public static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

        [DllImport("shell32.dll")]
        public static extern void ILFree(IntPtr pidl);

        #endregion
    }
}
