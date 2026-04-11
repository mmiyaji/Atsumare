using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Atsumare;

internal static class WindowIconHelper
{
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    internal static void Apply(Window window)
    {
        try
        {
            var iconPath = ResolveIconPath();
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
                return;

            var hwnd = WindowNative.GetWindowHandle(window);
            if (hwnd == IntPtr.Zero)
                return;

            var smallIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            var bigIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (smallIcon != IntPtr.Zero)
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, smallIcon);
            if (bigIcon != IntPtr.Zero)
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, bigIcon);
        }
        catch (Exception ex)
        {
            App.LogVerbose($"[ICON] Apply window icon failed: {ex}");
        }
    }

    private static string? ResolveIconPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico"),
            Path.Combine(AppContext.BaseDirectory, "tray.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.scale-200.png")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hInst,
        string name,
        uint type,
        int cx,
        int cy,
        uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
