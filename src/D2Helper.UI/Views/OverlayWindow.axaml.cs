using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace D2Helper.UI.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        Opened += (_, _) => PositionTopRight();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void PositionTopRight()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        var area = screen.WorkingArea;
        // Розташовуємо у верхньому правому куті з невеликим offset'ом
        // (вниз 80px — щоб не накривати FPS/ping HUD).
        var scale = screen.Scaling;
        var w = (int)(Width * scale);
        Position = new PixelPoint(area.X + area.Width - w - 16, area.Y + 80);
    }
}
