using System;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Microsoft.UI.Xaml;

namespace Atsumare
{
    internal static class WindowHider
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        private const int SW_HIDE = 0;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public static void HideAndRemoveFromAltTab(Window w)
        {
            var hwnd = WindowNative.GetWindowHandle(w);
            if (hwnd == IntPtr.Zero) return;

            // Alt+Tabから外す（ToolWindow扱いに）
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);

            // 非表示
            ShowWindow(hwnd, SW_HIDE);
        }
    }
}