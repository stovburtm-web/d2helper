using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace D2Helper.UI.Views;

public partial class OverlayWindow : Window
{
    private DispatcherTimer? _hotkeyTimer;
    private bool _prevHotkeyDown;
    private bool _overlayVisible = true;

    public OverlayWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            PositionTopRight();
            MakeClickThrough();
            StartHotkeyWatcher();
        };
        Closed += (_, _) => _hotkeyTimer?.Stop();
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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_MENU = 0x12; // Alt
    private const int VK_F9 = 0x78;

    private void MakeClickThrough()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var ex = GetWindowLong(handle, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(handle, GWL_EXSTYLE, ex);
    }

    /// <summary>
    /// Глобальний хоткей Alt+F9 — вмикає/вимикає overlay. Реалізовано через
    /// polling GetAsyncKeyState (100ms) бо це працює навіть коли фокус у Доті,
    /// не вимагає RegisterHotKey + WndProc підписки, і не блокує клавіатуру
    /// для гри (на відміну від глобального хука).
    /// </summary>
    private void StartHotkeyWatcher()
    {
        if (!OperatingSystem.IsWindows()) return;
        _hotkeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hotkeyTimer.Tick += (_, _) =>
        {
            var alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            var f9 = (GetAsyncKeyState(VK_F9) & 0x8000) != 0;
            var down = alt && f9;
            // Edge-detect: реагуємо лише на момент натискання.
            if (down && !_prevHotkeyDown) ToggleVisibility();
            _prevHotkeyDown = down;
        };
        _hotkeyTimer.Start();
    }

    private void ToggleVisibility()
    {
        _overlayVisible = !_overlayVisible;
        // Не закриваємо вікно (бо втратимо click-through на наступному Show),
        // а лише робимо його повністю прозорим через Opacity.
        Opacity = _overlayVisible ? 1.0 : 0.0;
    }
}
