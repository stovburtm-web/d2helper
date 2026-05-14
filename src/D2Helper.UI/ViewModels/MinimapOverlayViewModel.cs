using System.Reactive.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2Helper.Core.Gsi;
using D2Helper.Vision;
using Dota2GSI.Nodes;
using System.Runtime.Versioning;

namespace D2Helper.UI.ViewModels;

/// <summary>
/// View-model для <c>MinimapOverlayWindow</c> — окремого drag-able вікна,
/// яке показує захоплену мінімапу Dota з накладеною fog-маскою.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class MinimapOverlayViewModel : ObservableObject, IDisposable
{
    private readonly VisionLoop _vision;
    private readonly GameStateBus _gsi;
    private IDisposable? _framesSub;
    private IDisposable? _gsiSub;

    /// <summary>Зображення кропу мінімапи з накладеною напівпрозорою маскою fog (червоне поверх темного).</summary>
    [ObservableProperty] private Bitmap? _frame;

    /// <summary>Текст статусу — "ROI 256×256 · fog 38%".</summary>
    [ObservableProperty] private string _status = "vision: starting…";

    /// <summary>Координати пана-точки гравця всередині overlay (0..widget size). Null коли GSI не дає позиції.</summary>
    [ObservableProperty] private double _playerX = -100;
    [ObservableProperty] private double _playerY = -100;

    /// <summary>True якщо гравець за Dire і мінімапа розгорнута 180°. Береться з GSI + profile.</summary>
    [ObservableProperty] private bool _isRotated;

    /// <summary>Розмір кропу для widget — рендеримо в native розмір frame.</summary>
    [ObservableProperty] private int _viewWidth = 256;
    [ObservableProperty] private int _viewHeight = 256;

    public MinimapOverlayViewModel(VisionLoop vision, GameStateBus gsi)
    {
        _vision = vision;
        _gsi = gsi;

        _framesSub = _vision.Frames
            .Subscribe(f => Dispatcher.UIThread.Post(() => OnFrame(f)));

        // Слухаємо GSI щоб (а) знати команду гравця для rotation, (б) малювати пін.
        _gsiSub = _gsi.States
            .Sample(TimeSpan.FromMilliseconds(250))
            .Subscribe(gs => Dispatcher.UIThread.Post(() => OnGameState(gs)));
    }

    private void OnFrame(VisionFrame f)
    {
        try
        {
            // Композитуємо ROI з fog-overlay в один Bitmap для UI.
            using var roi = f.Roi;
            var detector = new FogMaskDetector();
            using var overlay = detector.RenderOverlay(f.FogMask);
            using var ms = new MemoryStream();
            using (var composite = new System.Drawing.Bitmap(roi.Width, roi.Height))
            {
                using var g = System.Drawing.Graphics.FromImage(composite);
                g.DrawImage(roi, 0, 0);
                g.DrawImage(overlay, 0, 0);
                composite.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            }
            ms.Position = 0;
            Frame?.Dispose();
            Frame = new Bitmap(ms);
            ViewWidth = roi.Width;
            ViewHeight = roi.Height;
            Status = $"ROI {roi.Width}×{roi.Height} · fog {f.FogPercent:F0}%";
        }
        catch (Exception ex)
        {
            Status = "frame err: " + ex.Message;
        }
    }

    private void OnGameState(Dota2GSI.GameState gs)
    {
        var profile = _vision.Profile;
        if (profile is null) return;
        IsRotated = profile.IsRotated180;

        var hero = gs.Hero.LocalPlayer;
        var x = hero.Location.X;
        var y = hero.Location.Y;
        if (x == 0 && y == 0) { PlayerX = -100; PlayerY = -100; return; }

        var (px, py) = WorldToMinimap.ToMinimap(x, y, ViewWidth, ViewHeight, IsRotated);
        PlayerX = px - 5; // центруємо точку 10×10
        PlayerY = py - 5;
    }

    [RelayCommand]
    private void Calibrate()
    {
        // V1: проста кнопка калібрування — захоплюємо повний клієнт Dota,
        // викликаємо CalibrationWindow.ShowDialog (TBD у наступному кроці).
        // Поки що — просто скидаємо у дефолт.
        var client = DotaWindowLocator.GetClientRect();
        if (client is null) { Status = "Dota window not found"; return; }
        var profile = CalibrationProfile.Default(client.Value.Width, client.Value.Height);
        _vision.SetProfile(profile);
        Status = $"calibrated → default for {client.Value.Width}×{client.Value.Height}";
    }

    [RelayCommand]
    private void ToggleRotation()
    {
        var current = _vision.Profile ?? CalibrationProfile.Default(1920, 1080);
        var flipped = current with { IsRotated180 = !current.IsRotated180 };
        _vision.SetProfile(flipped);
        IsRotated = flipped.IsRotated180;
        Status = $"rotation: {(flipped.IsRotated180 ? "Dire" : "Radiant")}";
    }

    public void Dispose()
    {
        _framesSub?.Dispose();
        _gsiSub?.Dispose();
        Frame?.Dispose();
    }
}
