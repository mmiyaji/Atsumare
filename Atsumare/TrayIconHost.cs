using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using System.IO;
namespace Atsumare
{
    internal sealed class TrayIconHost : IDisposable
    {
        private readonly Action _onShow;
        private readonly Action _onExit;


        public TrayIconHost(Action onShow, Action onExit)
        {
            _onShow = onShow;
            _onExit = onExit;
        }
        private static void Log(string s)
        {
            Debug.WriteLine("[Tray] " + s);
        }

        // コールバック用の隠しウィンドウ
        private IntPtr _hwnd = IntPtr.Zero;
        private WndProc? _wndProc;

        // NOTIFYICON
        private uint _taskbarCreatedMsg;
        private bool _added;

        // メニューID
        private const uint IDM_SHOW = 1001;
        private const uint IDM_EXIT = 1002;

        // NotifyIcon callback message
        private const int WM_TRAY = WM_APP + 1;

        // Click messages
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_COMMAND = 0x0111;

        // Win32 constants
        private const int WM_APP = 0x8000;
        private const int WM_DESTROY = 0x0002;

        private const int SW_SHOWNORMAL = 1;
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(
            IntPtr hInst,
            string lpszName,
            uint uType,
            int cxDesired,
            int cyDesired,
            uint fuLoad);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const uint LR_DEFAULTSIZE = 0x00000040;
        private const uint LR_SHARED = 0x00008000;

        public void Create()
        {
            if (_hwnd != IntPtr.Zero) return;

            Log("Create() start");

            _wndProc = WindowProc;

            _hwnd = CreateHiddenTopLevelWindow(_wndProc);
            Log($"CreateHiddenTopLevelWindow hwnd=0x{_hwnd.ToInt64():X} lastErr={Marshal.GetLastWin32Error()}");

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create tray host window.");

            _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
            Log($"TaskbarCreatedMsg={_taskbarCreatedMsg}");

            AddIcon();
            Log("Create() end");
        }
        private void AddIcon()
        {
            Log("AddIcon() start");

            string iconPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "tray.ico");

            Log("Loading tray icon from: " + iconPath);

            var hIcon = LoadImage(
                IntPtr.Zero,
                iconPath,
                IMAGE_ICON,
                0,
                0,
                LR_LOADFROMFILE | LR_DEFAULTSIZE);

            if (hIcon == IntPtr.Zero)
            {
                Log("LoadImage failed. lastErr=" + Marshal.GetLastWin32Error());
                // fallback
                hIcon = LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
            }
            Log($"LoadIcon hIcon=0x{hIcon.ToInt64():X} lastErr={Marshal.GetLastWin32Error()}");

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAY,
                hIcon = hIcon,
                szTip = "Atsumare"
            };

            bool okAdd = Shell_NotifyIcon(NIM_ADD, ref nid);
            Log($"Shell_NotifyIcon(NIM_ADD) ok={okAdd} lastErr={Marshal.GetLastWin32Error()} cbSize={nid.cbSize} hwnd=0x{nid.hWnd.ToInt64():X} msg=0x{nid.uCallbackMessage:X}");

            _added = okAdd;

            nid.uVersion = NOTIFYICON_VERSION_4;
            bool okVer = Shell_NotifyIcon(NIM_SETVERSION, ref nid);
            Log($"Shell_NotifyIcon(NIM_SETVERSION) ok={okVer} lastErr={Marshal.GetLastWin32Error()}");

            Log("AddIcon() end");
        }

        private void RemoveIcon()
        {
            if (!_added) return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1
            };

            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _added = false;
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAY)
            {
                Log($"WM_TRAY received wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X}");
            }
            else if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
            {
                Log("TaskbarCreated received (Explorer restarted?)");
            }
            else if (msg == WM_COMMAND)
            {
                Log($"WM_COMMAND wParam=0x{wParam.ToInt64():X}");
            }
            // Explorer再起動対策：タスクバー作り直し時にアイコン再登録
            if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
            {
                AddIcon();
                return IntPtr.Zero;
            }

            if (msg == WM_TRAY)
            {
                int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);

                if (mouseMsg == WM_LBUTTONUP)
                {
                    Log("Left click -> RequestToggle");
                    _onShow(); // = App.RequestShow()
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    Log("Right click -> Menu");
                    ShowContextMenu();
                }
                return IntPtr.Zero;
            }

            if (msg == WM_COMMAND)
            {
                uint id = (uint)(wParam.ToInt32() & 0xFFFF);
                Log($"WM_COMMAND id={id}");

                if (id == IDM_SHOW)
                {
                    Log("Menu -> RequestToggle");
                    _onShow();
                }
                if (id == IDM_EXIT)
                {
                    Log("Menu -> Exit");
                    _onExit();
                    return IntPtr.Zero;
                }
            }

            if (msg == WM_DESTROY)
            {
                RemoveIcon();
                return IntPtr.Zero;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            Log("ShowContextMenu() called");
            // 重要：メニューを出す前に foreground にする
            SetForegroundWindow(_hwnd);

            // カーソル位置
            GetCursorPos(out var pt);

            var hMenu = CreatePopupMenu();
            try
            {
                AppendMenu(hMenu, MF_STRING, IDM_SHOW, "表示");
                AppendMenu(hMenu, MF_SEPARATOR, 0, null);
                AppendMenu(hMenu, MF_STRING, IDM_EXIT, "終了");

                TrackPopupMenuEx(
                    hMenu,
                    TPM_RIGHTBUTTON,
                    pt.X,
                    pt.Y,
                    _hwnd,
                    IntPtr.Zero);
                PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        public void Dispose()
        {
            try { RemoveIcon(); } catch { }

            if (_hwnd != IntPtr.Zero)
            {
                try { DestroyWindow(_hwnd); } catch { }
                _hwnd = IntPtr.Zero;
            }
        }

        // ===== Win32 plumbing =====

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public IntPtr lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;

            // 便宜プロパティ
            public uint uVersion { get => uTimeoutOrVersion; set => uTimeoutOrVersion = value; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_DELETE = 0x00000002;
        private const uint NIM_SETVERSION = 0x00000004;

        private const uint NOTIFYICON_VERSION_4 = 4;

        private const int IDI_APPLICATION = 32512;

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;

        private const uint TPM_RIGHTBUTTON = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int X, int Y, int nWidth, int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
        private const int WM_NULL = 0x0000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const uint WS_OVERLAPPED = 0x00000000;
        private const int SW_HIDE = 0;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static IntPtr CreateHiddenTopLevelWindow(WndProc proc)
        {
            string cls = "AtsumareTrayHost_" + Guid.NewGuid().ToString("N");

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = proc,
                lpszClassName = cls,
                hInstance = GetModuleHandle(null),
                lpszMenuName = IntPtr.Zero
            };

            ushort atom = RegisterClassEx(ref wc);
            Log($"RegisterClassEx atom={atom} lastErr={Marshal.GetLastWin32Error()} class={cls}");

            var hwnd = CreateWindowEx(
                0,
                cls,
                "",
                WS_OVERLAPPED,
                0, 0, 0, 0,
                IntPtr.Zero,
                IntPtr.Zero,
                wc.hInstance,
                IntPtr.Zero);

            Log($"CreateWindowEx hwnd=0x{hwnd.ToInt64():X} lastErr={Marshal.GetLastWin32Error()}");

            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SW_HIDE);

            return hwnd;
        }
    }
}