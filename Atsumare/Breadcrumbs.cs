using System;
using System.Collections.Generic;

namespace Atsumare;

internal static class Breadcrumbs
{
    private static readonly object _gate = new();
    private static readonly Queue<string> _q = new();
    private const int Max = 80;

    public static void Add(string msg)
    {
        lock (_gate)
        {
            if (_q.Count >= Max) _q.Dequeue();
            _q.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {msg}");
        }
        CrashLog.Write("[BC] " + msg);
    }

    public static string Dump()
    {
        lock (_gate) return string.Join("\r\n", _q);
    }
}