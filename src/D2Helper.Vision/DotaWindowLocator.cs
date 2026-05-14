using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace D2Helper.Vision;

/// <summary>
/// Знаходить вікно процесу dota2.exe для capture. Працює без фокусу/активації.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DotaWindowLocator
{
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>Повертає головне видиме hwnd процесу dota2.exe, або IntPtr.Zero якщо гра не запущена.</summary>
    public static IntPtr Find()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("dota2"))
            {
                using (p)
                {
                    var h = p.MainWindowHandle;
                    if (h != IntPtr.Zero && IsWindowVisible(h)) return h;
                }
            }
        }
        catch
        {
            // ignore — гри може не бути
        }
        return IntPtr.Zero;
    }

    /// <summary>Поточний клієнтський прямокутник вікна Dota (в screen-coords) — або null якщо гри нема.</summary>
    public static System.Drawing.Rectangle? GetClientRect()
    {
        var hwnd = Find();
        if (hwnd == IntPtr.Zero) return null;
        if (!GetClientRect(hwnd, out var rc)) return null;
        var topLeft = new POINT { X = rc.Left, Y = rc.Top };
        var bottomRight = new POINT { X = rc.Right, Y = rc.Bottom };
        ClientToScreen(hwnd, ref topLeft);
        ClientToScreen(hwnd, ref bottomRight);
        return System.Drawing.Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
}
