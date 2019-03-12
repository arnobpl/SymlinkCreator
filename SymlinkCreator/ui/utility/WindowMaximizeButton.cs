using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SymlinkCreator.ui.utility
{
    internal static class WindowMaximizeButton
    {
        #region members

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBOX = 0x10000;

        #endregion


        #region methods

        /// <summary>
        /// Disables window's Maximize button. Call this method inside the window's SourceInitialized event.
        /// </summary>
        /// <param name="window">Window whose Maximize button to be disable</param>
        public static void DisableMaximizeButton(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var value = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, (value & ~WS_MAXIMIZEBOX));
        }

        #endregion
    }
}