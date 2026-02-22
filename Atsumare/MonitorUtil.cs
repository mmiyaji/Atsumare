using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Atsumare
{
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