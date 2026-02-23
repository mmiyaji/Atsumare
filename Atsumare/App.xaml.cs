using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        private static int _toggleRequested;
        private static int _showRequested; // 0/1 (Interlocked)
        private bool _toggleBusy;         // UIスレッド専用
        private static int _showBusy; // 0/1

        private DispatcherQueueTimer? _pollTimer;

        // ★追加：UI DispatcherQueue を保持して、外部通知を確実にUIへ投げる
        private DispatcherQueue? _uiQueue;

        // ★追加：外部通知待受の停止用
        private CancellationTokenSource? _showListenCts;
        private static readonly object _logLock = new();
        private static long _suppressAutoCloseUntilTick;
        private static int _fatalHandling; // 再入防止

        public App()
        {
            InitializeComponent();
        }

        internal static void RequestToggle()
        {
            Interlocked.Exchange(ref _toggleRequested, 1);
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
            // ★UIスレッドの DispatcherQueue を保持
            _uiQueue = DispatcherQueue.GetForCurrentThread();
            _showListenCts = new CancellationTokenSource();

            RegisterGlobalExceptionHandlers();

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
            // 既存からの「表示要求」を待つ（UIへ確実に投げる）
            _ = WaitForExternalShowRequestsAsync(_showListenCts.Token);

            _keepAlive = new KeepAliveWindow();
            _keepAlive.Activate();
            WindowHider.HideAndRemoveFromAltTab(_keepAlive);

            _tray = new TrayIconHost(
                onShow: () => Interlocked.Exchange(ref _showRequested, 1),
                onExit: () =>
                {
                    // Exitもトレイスレッドから直接やらない
                    _uiQueue?.TryEnqueue(() => ExitApplication());
                }
            );
            _tray.Create();

            // ③ ホットキー登録（Ctrl+Alt+Space 例）
            _hotkey = new HotkeyHost();
            _hotkey.HotkeyPressed += (_, __) => App.RequestToggle();
            _hotkey.StartRegisterHotkey(
                modifiers: HotkeyModifiers.Control | HotkeyModifiers.Alt,
                virtualKey: HotkeyVKey.Space
            );

            var dq = DispatcherQueue.GetForCurrentThread();

            _pollTimer = dq.CreateTimer();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(200);
            _pollTimer.IsRepeating = true;
            _pollTimer.Tick += (_, __) =>
            {
                // トレイ/多重起動からの「表示要求」：UIスレッドで処理
                if (Interlocked.Exchange(ref _showRequested, 0) == 1)
                {
                    ShowFromExternalOrTray(); // ← Showのみ（前面化/表示）
                }

                // ホットキー等のトグルは従来通り
                if (Interlocked.Exchange(ref _toggleRequested, 0) == 1)
                {
                    RequestToggleOnUI();
                }
            };
            _pollTimer.Start();
            LogLine("[BOOT] OnLaunched completed");
        }

        /// <summary>
        /// トレイ/外部通知からの「表示」を統一入口にする
        /// 既に表示中なら前面化のみ（閉じない）
        /// </summary>
        private void ShowFromExternalOrTray()
        {
            if (Interlocked.Exchange(ref _showBusy, 1) == 1)
                return;

            try
            {
                SuppressAutoCloseFor(1200);
                if (OpenWindows.Count > 0)
                {
                    foreach (var w in OpenWindows.ToArray())
                    {
                        try { w.Activate(); } catch { }
                    }
                    return;
                }

                ShowPickerOnAllMonitors();
            }
            finally
            {
                Interlocked.Exchange(ref _showBusy, 0);
            }
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
        private void RegisterGlobalExceptionHandlers()
        {
            // UIスレッドで未処理になった例外
            this.UnhandledException += (_, e) =>
            {
                try
                {
                    LogException("App.UnhandledException", e.Exception);
                }
                catch { }

                // ここで true にすると落ちないが、状態不整合のまま走る危険もある
                // 常駐ツールなら「ログを書いた上で安全に終了」が基本
                e.Handled = true;
                TriggerFatalShutdown("App.UnhandledException");
            };

            // バックグラウンドTask等の未観測例外
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try
                {
                    LogException("TaskScheduler.UnobservedTaskException", e.Exception);
                }
                catch { }

                e.SetObserved();
                TriggerFatalShutdown("TaskScheduler.UnobservedTaskException");
            };

            // それ以外（最終防衛線）
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    if (e.ExceptionObject is Exception ex)
                        LogException("AppDomain.UnhandledException", ex);
                    else
                        LogLine("AppDomain.UnhandledException: non-Exception object");
                }
                catch { }

                // ここはもうUIスレッド保証がないので、できるだけ即終了
                Environment.Exit(unchecked((int)0xDEAD));
            };
        }

        private void TriggerFatalShutdown(string reason)
        {
            if (Interlocked.Exchange(ref _fatalHandling, 1) == 1)
                return;

            try
            {
                LogLine($"[FATAL] TriggerFatalShutdown reason={reason}");
            }
            catch { }

            // UIスレッドに寄せて後始末
            try
            {
                _uiQueue?.TryEnqueue(() =>
                {
                    try { ExitApplication(); }
                    catch { Exit(); }
                });
            }
            catch
            {
                try { Exit(); } catch { }
            }
        }
        /// <summary>
        /// 外部（2個目起動）からのShow要求を待ち、UIスレッドへ確実に投げる
        /// </summary>
        private async Task WaitForExternalShowRequestsAsync(CancellationToken ct)
        {
            if (_single == null) return;

            while (!ct.IsCancellationRequested)
            {
                await _single.WaitForShowRequestAsync().ConfigureAwait(false);

                var q = _uiQueue;
                if (q == null) continue;

                // ★二重起動時は Show（未表示なら表示、表示中なら前面化）
                _ = q.TryEnqueue(() => ShowFromExternalOrTray());
            }
        }

        internal static void ShowPickerOnAllMonitors()
        {
            SuppressAutoCloseFor(1200);

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
                // ★外部通知待受を止める
                try { _showListenCts?.Cancel(); } catch { }
                _showListenCts = null;

                // タイマー停止（任意だけど安全）
                try { _pollTimer?.Stop(); } catch { }
                _pollTimer = null;

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
        internal static void SuppressAutoCloseFor(int ms)
        {
            var until = Environment.TickCount64 + ms;
            Interlocked.Exchange(ref _suppressAutoCloseUntilTick, until);
        }

        internal static bool IsAutoCloseSuppressed()
        {
            var until = Interlocked.Read(ref _suppressAutoCloseUntilTick);
            return Environment.TickCount64 < until;
        }

        private static string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Atsumare",
                "logs",
                $"app-{DateTime.Now:yyyyMMdd}.log");

        private static void LogLine(string message)
        {
            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff}\t{message}{Environment.NewLine}";
                lock (_logLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static void LogException(string where, Exception ex)
        {
            LogLine($"[EX] {where}: {ex.GetType().FullName}: {ex.Message}");
            LogLine(ex.StackTrace ?? "");
            if (ex.InnerException != null)
            {
                LogLine($"[EX] Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                LogLine(ex.InnerException.StackTrace ?? "");
            }
        }

    }
}