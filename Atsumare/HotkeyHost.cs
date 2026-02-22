using System;
using System.Runtime.InteropServices;

namespace Atsumare
{
    [Flags]
    internal enum HotkeyModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000, // 連打抑止（任意）
    }

    internal enum HotkeyVKey : uint
    {
        Space = 0x20,
        // 必要に応じて追加
    }

    internal sealed class HotkeyHost : IDisposable
    {
        public event EventHandler? HotkeyPressed;

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0xA75; // 適当な固定ID

        private IntPtr _hwnd = IntPtr.Zero;
        private WndProc? _wndProc;

        public void StartRegisterHotkey(HotkeyModifiers modifiers, HotkeyVKey virtualKey)
        {
            if (_hwnd != IntPtr.Zero)
                return;

            _wndProc = WindowProc;
            _hwnd = CreateHiddenTopLevelWindow(_wndProc);

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create message-only window.");

            bool ok = RegisterHotKey(_hwnd, HOTKEY_ID, (uint)modifiers, (uint)virtualKey);
            if (!ok)
            {
                var err = Marshal.GetLastWin32Error();
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
                throw new InvalidOperationException($"RegisterHotKey failed. err={err}");
            }
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, HOTKEY_ID);
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }

        // ---- Win32 message-only window ----

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
            public IntPtr lpszMenuName;      // ★IntPtr
            public string lpszClassName;     // ★string
            public IntPtr hIconSm;
        }

        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

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

            RegisterClassEx(ref wc);

            // ★親なし（トップレベル）で作る
            var hwnd = CreateWindowEx(
                0,
                cls,
                "",
                WS_OVERLAPPED,
                0, 0, 0, 0,
                IntPtr.Zero,   // ★ここが重要（HWND_MESSAGEではない）
                IntPtr.Zero,
                wc.hInstance,
                IntPtr.Zero);

            // ★表示しない
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SW_HIDE);

            return hwnd;
        }
    }
}