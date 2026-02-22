using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinRT.Interop;
namespace Atsumare;

public sealed partial class MainWindow : Window
{
    private readonly OverlayManager _overlayManager = new();

    public MainWindow()
    {
        this.InitializeComponent();
    }

    private void ShowOverlay_Click(object sender, RoutedEventArgs e)
    {
        _overlayManager.ToggleAllMonitors();
    }
    private void TestMoveChrome_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Chrome/Edge のトップレベルウィンドウを1つ取得
            var hwnd = Win32WindowFinder.FindFirstTopLevelWindowByProcessName(new[] { "chrome", "msedge" });
            if (hwnd == IntPtr.Zero)
            {
                Debug.WriteLine("Chrome/Edge window not found.");
                return;
            }

            // 次のモニターへ移動（同一モニター内移動ではなく “別モニター” があるならそちらへ）
            if (!Win32MonitorMover.MoveWindowToNextMonitor(hwnd))
            {
                // モニターが1枚などで “次” が無ければ、同一モニター内で少しだけ移動（切り分け用）
                Win32MonitorMover.NudgeWindow(hwnd, dx: 200, dy: 0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
