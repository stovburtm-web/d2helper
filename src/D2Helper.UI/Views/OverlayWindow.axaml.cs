using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace D2Helper.UI.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            PositionTopRight();
            MakeClickThrough();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void PositionTopRight()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var area = screen.WorkingArea;
        // Правий верхній кут; Y=80 щоб не накривати FPS/ping HUD в Dota.
        var scale = screen.Scaling;
        var w = (int)(Width * scale);
        // Якщо Width ще NaN (SizeToContent) — візьмемо її після першого лейауту.
        if (double.IsNaN(Width)) w = (int)(440 * scale);
        Position = new PixelPoint(area.X + area.Width - w - 16, area.Y + 80);
    }

    // === Win32 click-through ===
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    private void MakeClickThrough()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var ex = GetWindowLong(handle, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(handle, GWL_EXSTYLE, ex);
    }
}
