using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Atsumare
{
    public partial class App : Application
    {
        internal static readonly List<MainWindow> OpenWindows = new();
        internal static class AppState
        {
            public static volatile bool Bootstrapping = true;
        }
        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var monitors = MonitorUtil.GetAllMonitors();

            if (monitors.Count <= 1)
            {
                var w = new MainWindow();
                if (monitors.Count == 1) w.InitializeForMonitor(monitors[0]);
                OpenWindows.Add(w);
                w.Activate();

                // 起動直後の切替が落ち着いたら有効化
                _ = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
                {
                    AppState.Bootstrapping = false;
                });
                return;
            }

            foreach (var mon in monitors)
            {
                var w = new MainWindow();
                w.InitializeForMonitor(mon);
                OpenWindows.Add(w);
                w.PrePositionToMonitorCenter(mon, 820, 540);
                //w.MoveToMonitorCenter(mon, 820, 540);
                w.Activate();
            }

            // ★重要：複数Activate直後はフォーカスが揺れるので、少し待ってから自動クローズを有効化
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var timer = dq.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(300);
            timer.IsRepeating = false;
            timer.Tick += (_, __) =>
            {
                AppState.Bootstrapping = false;
                timer.Stop();
            };
            timer.Start();
        }

    }

    internal static class MonitorUtil
    {
        private delegate bool EnumMonitorsProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsProc lpfnEnum, IntPtr dwData);

        internal static List<IntPtr> GetAllMonitors()
        {
            var list = new List<IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, __, ___) =>
            {
                list.Add(hMon);
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}