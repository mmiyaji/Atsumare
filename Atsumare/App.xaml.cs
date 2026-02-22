using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Atsumare
{
    public partial class App : Application
    {
        // 表示用ウィンドウ群（常駐ホストは別）
        internal static readonly List<MainWindow> OpenWindows = new();

        private SingleInstanceManager? _single;
        private HotkeyHost? _hotkey;
        private KeepAliveWindow? _keepAlive;
        private TrayIconHost? _tray;
        private static int _showRequested; // 0/1 (Interlockedで使う)
        private static int _toggleRequested;
        private bool _toggleBusy;         // UIスレッド専用
        private bool _togglePending;      // UIスレッド専用（連打の合図）
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _pollTimer;

        public App()
        {
            InitializeComponent();
        }


        internal static void RequestToggle()
        {
            System.Threading.Interlocked.Exchange(ref _toggleRequested, 1);
        }
        private void RequestToggleOnUI()
        {
            if (_toggleBusy)
                return;

            _toggleBusy = true;

            try
            {
                if (OpenWindows.Count > 0)
                {
                    // Closeだけして終わる
                    CloseAllPickerWindows();
                    return;
                }

                // Showは次のDispatcherで実行（Closeと分離）
                DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
                {
                    try
                    {
                        ShowPickerOnAllMonitors();
                    }
                    finally
                    {
                        _toggleBusy = false;
                    }
                });

                return;
            }
            finally
            {
                // Closeの場合だけここに来る
                if (OpenWindows.Count == 0)
                    _toggleBusy = false;
            }
        }
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _single = new SingleInstanceManager(
                mutexName: @"Global\Atsumare_Mutex_v1",
                signalName: @"Global\Atsumare_ShowSignal_v1"
            );

            // ② 多重起動防止：2個目は既存へ「表示要求」して終了
            if (!_single.TryEnterAsFirstInstance())
            {
                _single.SignalShowRequest();
                Exit(); // ← 常駐済みなので自分は終了
                return;
            }

            // ① 常駐ホスト化：起動してもUIは出さない
            // 既存からの「表示要求」を待つ
            _ = WaitForExternalShowRequestsAsync();

            _keepAlive = new KeepAliveWindow();
            _keepAlive.Activate();
            WindowHider.HideAndRemoveFromAltTab(_keepAlive);

            _tray = new TrayIconHost(
                onShow: () => App.RequestToggle(),
                onExit: () => ExitApplication()
            );
            _tray.Create();
            // ③ ホットキー登録（Ctrl+Alt+Space 例）
            _hotkey = new HotkeyHost();
            _hotkey.HotkeyPressed += (_, __) => App.RequestToggle();
            _hotkey.StartRegisterHotkey(
                modifiers: HotkeyModifiers.Control | HotkeyModifiers.Alt,
                virtualKey: HotkeyVKey.Space
            );

            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            _pollTimer = dq.CreateTimer();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(200);
            _pollTimer.IsRepeating = true;
            _pollTimer.Tick += (_, __) =>
            {
                if (System.Threading.Interlocked.Exchange(ref _toggleRequested, 0) == 1)
                {
                    RequestToggleOnUI();
                }
            };
            _pollTimer.Start();
        }
        internal static void TogglePicker()
        {
            if (OpenWindows.Count > 0)
            {
                CloseAllPickerWindows(); // 表示ウィンドウだけ
            }
            else
            {
                ShowPickerOnAllMonitors();
            }
        }
        internal static void CloseAllPickerWindows()
        {
            var list = OpenWindows.ToArray();

            foreach (var w in list)
            {
                try
                {
                    if (w != null)
                        w.Close();
                }
                catch { }
            }
        }
        private async Task WaitForExternalShowRequestsAsync()
        {
            if (_single == null) return;

            while (true)
            {
                await _single.WaitForShowRequestAsync().ConfigureAwait(false);

                // UIスレッドで表示
                var dq = DispatcherQueue.GetForCurrentThread();
                _ = dq.TryEnqueue(() => ShowPickerOnAllMonitors());
            }
        }

        internal static void ShowPickerOnAllMonitors()
        {
            // すでに出ているなら前面化だけ（好みで：一旦閉じて出し直しでもOK）
            if (OpenWindows.Count > 0)
            {
                foreach (var w in OpenWindows.ToArray())
                {
                    try { w.Activate(); } catch { }
                }
                return;
            }

            var monitors = MonitorUtil.GetAllMonitors();
            if (monitors.Count == 0)
                return;

            foreach (var mon in monitors)
            {
                var w = new MainWindow();
                w.InitializeForMonitor(mon);

                OpenWindows.Add(w);

                // ★ちらつき対策：Activate前に Win32 で配置
                w.PrePositionToMonitorCenter(mon, 820, 540);

                w.Activate();
            }
        }
        private void ExitApplication()
        {
            try
            {
                // 表示中のAtsumareを閉じる
                var list = OpenWindows.ToArray();
                foreach (var w in list)
                {
                    try { w.Close(); } catch { }
                }
                OpenWindows.Clear();

                // ホットキー解除
                _hotkey?.Dispose();
                _hotkey = null;

                // トレイ解除
                _tray?.Dispose();
                _tray = null;

                // KeepAliveを閉じる（任意）
                try { _keepAlive?.Close(); } catch { }
                _keepAlive = null;
            }
            catch { }

            // プロセス終了
            Exit();
        }
    }
}