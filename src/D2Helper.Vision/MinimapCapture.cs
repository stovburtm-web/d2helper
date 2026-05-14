using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace D2Helper.Vision;

/// <summary>
/// Захоплює фрагмент вікна Dota 2 (область мінімапи) через Win32 <c>PrintWindow</c>.
/// На відміну від <c>BitBlt</c> з екрану, <c>PrintWindow</c> працює навіть коли вікно
/// перекрите іншими і не вимагає його активації — гра не втрачає фокус.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MinimapCapture : IDisposable
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private Bitmap? _fullWindow;
    private int _lastW, _lastH;

    /// <summary>
    /// Захоплює всю клієнтську область вікна Dota і вирізає ROI мінімапи.
    /// Повертає новий <see cref="Bitmap"/> ROI у форматі 32bppArgb. Викликач відповідальний за Dispose().
    /// Повертає null, якщо вікно не знайдене/мінімалізоване/розмір некоректний.
    /// </summary>
    /// <param name="hwnd">Handle вікна Dota (від <see cref="DotaWindowLocator.Find"/>).</param>
    /// <param name="minimapRect">Прямокутник у координатах **клієнтської області** вікна.</param>
    public Bitmap? CaptureRoi(IntPtr hwnd, Rectangle minimapRect)
    {
        if (hwnd == IntPtr.Zero) return null;
        if (!GetClientRect(hwnd, out var rc)) return null;

        int w = rc.Right - rc.Left;
        int h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return null;

        // Реюзаємо buffer-bitmap між кадрами — менше алокацій GC.
        if (_fullWindow is null || _lastW != w || _lastH != h)
        {
            _fullWindow?.Dispose();
            _fullWindow = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            _lastW = w; _lastH = h;
        }

        using (var g = Graphics.FromImage(_fullWindow))
        {
            var hdc = g.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)) return null;
            }
            finally { g.ReleaseHdc(hdc); }
        }

        // Кліпуємо ROI до меж — на випадок калібрування за межами вікна.
        var safe = Rectangle.Intersect(new Rectangle(0, 0, w, h), minimapRect);
        if (safe.Width < 8 || safe.Height < 8) return null;

        var roi = new Bitmap(safe.Width, safe.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(roi))
        {
            g.DrawImage(_fullWindow, new Rectangle(0, 0, safe.Width, safe.Height),
                safe, GraphicsUnit.Pixel);
        }
        return roi;
    }

    public void Dispose()
    {
        _fullWindow?.Dispose();
        _fullWindow = null;
    }
}
