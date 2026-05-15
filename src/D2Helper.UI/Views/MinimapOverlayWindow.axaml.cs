using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace D2Helper.UI.Views;

/// <summary>
/// Drag-able transparent top-most overlay з кропом мінімапи Dota.
/// На відміну від основного <see cref="OverlayWindow"/>, цей **не** click-through —
/// користувач повинен мати змогу drag/resize/калібрувати.
/// </summary>
public partial class MinimapOverlayWindow : Window
{
    private DispatcherTimer? _hotkeyTimer;
    private bool _prevHotkeyDown;
    // За замовчуванням debug-widget прихований (App.axaml.cs виставляє Opacity=0).
    // Alt+F10 toggle → перший прес показує його (Opacity=1).
    private bool _isVisible = false;

    public MinimapOverlayWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            PositionInitial();
            ApplyToolWindowStyle();
            // Стартуємо в click-through режимі — debug-widget схований і не повинен
            // блокувати кліки на ігрову мінімапу (під ним лежить heatmap-вікно).
            SetClickThrough(true);
            StartHotkeyWatcher();
        };
        Closed += (_, _) => _hotkeyTimer?.Stop();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void PositionInitial()
    {
        // Лівий нижній — рідне місце для мінімапи, не накриває overlay квестів справа-вгорі.
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var w = (int)(Width * scale);
        var h = (int)(Height * scale);
        Position = new PixelPoint(area.X + 16, area.Y + area.Height - h - 16);
    }

    /// <summary>Drag-by-header — без кастомного chrome. Не використовуємо BeginMoveDrag в OnPointerPressed
    /// на всю поверхню, бо інакше клік по кнопці теж починатиме drag.</summary>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // === Win32: робимо вікно tool-window (не з'являється в Alt+Tab і не активується по кліку) ===
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_MENU = 0x12;  // Alt
    private const int VK_F10 = 0x79;

    private void ApplyToolWindowStyle()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var ex = GetWindowLong(handle, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(handle, GWL_EXSTYLE, ex);
    }

    /// <summary>
    /// Перемикає WS_EX_TRANSPARENT / WS_EX_LAYERED на debug-widget вікні.
    /// Коли воно сховане (Opacity=0) — має пропускати кліки на ігрову мінімапу
    /// (інакше блокує click-through нашого heatmap-вікна що знаходиться під ним).
    /// Коли користувач показав його через Alt+F10 — знімаємо click-through, щоб
    /// він міг тягти/калібрувати.
    /// </summary>
    private void SetClickThrough(bool enable)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var ex = GetWindowLong(handle, GWL_EXSTYLE);
        if (enable)
            ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        else
            ex &= ~(WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SetWindowLong(handle, GWL_EXSTYLE, ex);
    }

    /// <summary>Глобальний хоткей Alt+F10 — показати/сховати vision overlay.</summary>
    private void StartHotkeyWatcher()
    {
        if (!OperatingSystem.IsWindows()) return;
        _hotkeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hotkeyTimer.Tick += (_, _) =>
        {
            var alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            var f10 = (GetAsyncKeyState(VK_F10) & 0x8000) != 0;
            var down = alt && f10;
            if (down && !_prevHotkeyDown) ToggleVisibility();
            _prevHotkeyDown = down;
        };
        _hotkeyTimer.Start();
    }

    private void ToggleVisibility()
    {
        _isVisible = !_isVisible;
        Opacity = _isVisible ? 1.0 : 0.0;
        // Видимий → можна тягти/калібрувати, знімаємо click-through.
        // Невидимий → click-through, інакше блокує кліки на ігрову мінімапу.
        SetClickThrough(!_isVisible);
    }
}
