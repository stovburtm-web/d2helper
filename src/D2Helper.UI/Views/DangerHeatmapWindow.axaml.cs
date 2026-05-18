using System.IO;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using D2Helper.Core.Gsi;
using D2Helper.Core.Models;
using D2Helper.Vision;
using Dota2GSI;
using DBitmap = System.Drawing.Bitmap;
using DImageFormat = System.Drawing.Imaging.ImageFormat;

namespace D2Helper.UI.Views;

/// <summary>
/// Прозорий click-through overlay, що рендерить тільки danger-heatmap
/// поверх ігрової мінімапи Dota. Без рамок, кнопок чи тексту.
///
/// Позиція й розмір беруться з <see cref="CalibrationProfile"/> +
/// <see cref="DotaWindowLocator.GetClientRect"/> кожні 500мс.
/// Якщо Dota не запущена або калібрування нема — вікно ховається.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class DangerHeatmapWindow : Window
{
    private readonly VisionLoop _vision;
    private readonly GameStateBus _gsi;
    private DispatcherTimer? _positionTimer;
    private IDisposable? _gsiSub;
    private IDisposable? _frameSub;
    private IDisposable? _deathSub;
    private IDisposable? _towerSub;

    private PlayerSide _side = PlayerSide.Radiant;
    private bool _sideLocked;
    private float _gameTime = 0f;
    private (float X, float Y)? _heroWorld;
    private float[,]? _fogField;
    private EmpiricalDeathField? _empiricalField;
    private readonly MinimapPresenceTracker _presenceTracker = new();
    private EnemyPresenceSnapshot? _presence;
    private EnemyPresenceSnapshot? _friendly;
    // V1.7: tower snapshot — для початку всі живі, поновлюється при TowerDestroyed.
    // Простий лічильник на стороно/лайн дає нам tier (1-й destroy на TopLane Dire = T1Top тощо).
    private TowerSnapshot _towers = TowerSnapshot.AllAlive();
    private readonly Dictionary<(TowerTeam, string), int> _towerDestroyCount = new();
    private DateTime _lastRender = DateTime.MinValue;
    private int _renderedW = -1, _renderedH = -1;
    private PlayerSide _renderedSide;
    private float _renderedGameTime = float.NaN;
    private bool _renderedRotated;
    private bool _dynamicDirty = true;

    private Image? _heatmapA;
    private Image? _heatmapB;
    private bool _useB; // в який буфер писати наступний кадр (false=A, true=B)
    private Canvas? _root;
    private bool _windowPlaced;
    private int _placedDotaLeft, _placedDotaTop, _placedW, _placedH;

    public DangerHeatmapWindow(VisionLoop vision, GameStateBus gsi)
    {
        _vision = vision;
        _gsi = gsi;
        InitializeComponent();
        _heatmapA = this.FindControl<Image>("HeatmapImageA");
        _heatmapB = this.FindControl<Image>("HeatmapImageB");
        _root = this.FindControl<Canvas>("Root");

        Opened += (_, _) =>
        {
            // Click-through застосовуємо ОДИН раз — як OverlayWindow з квестами.
            ApplyClickThroughStyle();
            // А також ПОВТОРНО через Background-prio dispatcher post — щоб виконатись
            // ПІСЛЯ усіх внутрішніх Avalonia style-mutations, які можуть скидати WS_EX.
            Dispatcher.UIThread.Post(ApplyClickThroughStyle, DispatcherPriority.Background);
            TryLoadEmpiricalField();
            StartPositionLoop();
            _gsiSub = _gsi.States
                .Sample(TimeSpan.FromMilliseconds(500))
                .Subscribe(gs => Dispatcher.UIThread.Post(() => OnGameState(gs)));
            _frameSub = _vision.Frames
                .Sample(TimeSpan.FromMilliseconds(750))
                .Subscribe(frame => Dispatcher.UIThread.Post(() => OnVisionFrame(frame)));
            // V1.5: ворожа смерть → помітити PlayerID як «мертвий до» в трекері. Це зменшує AliveEnemyCount
            // в EnemyPresenceSnapshot → absence-confidence досягає 1.0 з меншою к-стю fresh enemies.
            _deathSub = _gsi.PlayerDeaths
                .Subscribe(evt => Dispatcher.UIThread.Post(() => OnEnemyDeath(evt)));
            // V1.7: вежа впала → оновлюємо TowerSnapshot. GSI event дає тільки (team, lane) —
            // tier виводимо з порядку (1-й destroy на лайні = T1, 2-й = T2, 3-й = T3).
            _towerSub = _gsi.TowerDestroyed
                .Subscribe(evt => Dispatcher.UIThread.Post(() => OnTowerDestroyed(evt)));
        };
        Closed += (_, _) =>
        {
            _positionTimer?.Stop();
            _gsiSub?.Dispose();
            _frameSub?.Dispose();
            _deathSub?.Dispose();
            _towerSub?.Dispose();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ===== Win32 click-through + no-activate + tool-window =====
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    private void ApplyClickThroughStyle()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var before = GetWindowLong(handle, GWL_EXSTYLE);
        // Для коректного mouse pass-through на top-level вікні Windows вимагає обидва прапори:
        //   WS_EX_LAYERED — позначає вікно як layered (потрібно для hit-test routing),
        //   WS_EX_TRANSPARENT — каже не приймати mouse events і пропускати їх вглиб.
        // ВАЖЛИВО: НЕ викликаємо SetLayeredWindowAttributes / UpdateLayeredWindow —
        // тоді DWM-композиція Avalonia (TransparencyLevelHint=Transparent) лишається активною.
        var want = before | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(handle, GWL_EXSTYLE, want);
        var verify = GetWindowLong(handle, GWL_EXSTYLE);
        bool transparentOn = (verify & WS_EX_TRANSPARENT) != 0;
        bool layeredOn = (verify & WS_EX_LAYERED) != 0;
        Console.WriteLine($"[HEATMAP] ApplyClickThrough hwnd=0x{handle:X} before=0x{before:X8} after=0x{verify:X8} T={transparentOn} L={layeredOn}");
    }

    private PixelPoint _screenOrigin;
    private double _screenScale = 1.0;

    // ===== Position loop (follows Dota window) =====
    private void StartPositionLoop()
    {
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _positionTimer.Tick += (_, _) => UpdatePosition();
        _positionTimer.Start();
        UpdatePosition();
    }

    /// <summary>
    /// Один раз ставимо Position/Width/Height точно на ROI мінімапи Dota — як Quests
    /// <see cref="OverlayWindow"/>: фіксований розмір, фіксована позиція, після цього
    /// нічого не змінюємо.
    ///
    /// Якщо Dota вікно ПЕРЕМІСТИЛОСЬ або змінився профіль — переставляємо позицію (це
    /// рідка подія, не per-frame), і реапплаїмо click-through.
    /// </summary>
    private void UpdatePosition()
    {
        var clientRect = DotaWindowLocator.GetClientRect();
        var profile = _vision.Profile;
        if (clientRect is null || profile is null)
        {
            if (IsVisible) Hide();
            _windowPlaced = false;
            return;
        }

        int dotaLeftPx = clientRect.Value.Left + profile.Left;
        int dotaTopPx = clientRect.Value.Top + profile.Top;
        int wPx = profile.Width;
        int hPx = profile.Height;

        bool needsReplace = !_windowPlaced ||
            dotaLeftPx != _placedDotaLeft ||
            dotaTopPx != _placedDotaTop ||
            wPx != _placedW ||
            hPx != _placedH;

        if (needsReplace)
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            _screenScale = screen?.Scaling ?? 1.0;
            Position = new PixelPoint(dotaLeftPx, dotaTopPx);
            Width = wPx / _screenScale;
            Height = hPx / _screenScale;
            _placedDotaLeft = dotaLeftPx;
            _placedDotaTop = dotaTopPx;
            _placedW = wPx;
            _placedH = hPx;
            _windowPlaced = true;
            if (!IsVisible) Show();
            // Після зміни Position/Size Avalonia скидає extended-style → реапплай.
            ApplyClickThroughStyle();
            Dispatcher.UIThread.Post(ApplyClickThroughStyle, DispatcherPriority.Background);
        }
        else if (!IsVisible)
        {
            Show();
            ApplyClickThroughStyle();
        }
        else
        {
            // Постійний reapply — як страхування проти будь-яких внутрішніх style mutations Avalonia.
            ApplyClickThroughStyle();
        }

        EnsureRendered(wPx, hPx, profile.IsRotated180);
    }

    private void OnGameState(GameState gs)
    {
        try
        {
            // gameTime в секундах.
            _gameTime = gs.Map?.GameTime ?? 0;

            // === Сторона гравця: визначаємо ОДИН раз і фіксуємо ===
            // GSI Player.LocalPlayer.Team — енум PlayerTeam. Перед піком героя він
            // приходить як Unassigned/Spectator/None — ігноруємо такі значення.
            // Як тільки прилітає валідне Radiant/Dire — фіксуємо й більше не міняємо
            // навіть якщо в наступних пакетах поле "зіб'ється" (пауза, replay-frame).
            if (!_sideLocked)
            {
                var teamName = gs.Player?.LocalPlayer?.Team.ToString() ?? "";
                if (teamName.Equals("Radiant", StringComparison.OrdinalIgnoreCase))
                {
                    _side = PlayerSide.Radiant;
                    _sideLocked = true;
                }
                else if (teamName.Equals("Dire", StringComparison.OrdinalIgnoreCase))
                {
                    _side = PlayerSide.Dire;
                    _sideLocked = true;
                }
                // інакше — лишаємо дефолтне Radiant до першого валідного апдейту
                if (_sideLocked)
                {
                    Console.WriteLine($"[HEATMAP] Side locked = {_side}");
                }
            }

            // Власна позиція героя (тільки для hero-halo модифікатора, НЕ для визначення сторони).
            var hero = gs.Hero?.LocalPlayer;
            if (hero is not null)
            {
                var xv = hero.Location.X;
                var yv = hero.Location.Y;
                _heroWorld = (xv == 0 && yv == 0) ? null : (xv, yv);
            }
            else
            {
                _heroWorld = null;
            }

            // V1.3+V1.4: оновлюємо enemy presence (з ghost'ами) + ally force (без ghost).
            var force = _presenceTracker.UpdateForce(gs, _side == PlayerSide.Radiant, DateTime.UtcNow);
            _presence = force.Enemies;
            _friendly = force.Allies;

            _dynamicDirty = true;

            var profile = _vision.Profile;
            if (profile != null) EnsureRendered(profile.Width, profile.Height, profile.IsRotated180);
        }
        catch
        {
            // ignore — наступний тік виправить
        }
    }

    /// <summary>
    /// V1.5: GSI <c>PlayerDeathsChanged</c> для будь-якого гравця. Якщо це ворог (team != my side
    /// AND new > previous) — повідомляємо tracker'у. Це єдиний надійний спосіб дізнатись що ворог
    /// помер у normal play (HeroDied для ворогів не фаїрить — їх IsAlive не expose'ється).
    /// </summary>
    private void OnEnemyDeath(Dota2GSI.EventMessages.PlayerDeathsChanged evt)
    {
        try
        {
            if (evt is null || evt.New <= evt.Previous) return;
            var p = evt.Player;
            if (p is null) return;
            // Перевіряємо що це ВОРОГ (FullPlayerDetails.Details.Team — PlayerTeam enum).
            var team = p.Details?.Team.ToString() ?? "";
            bool isEnemy = (_side == PlayerSide.Radiant && team.Equals("Dire", StringComparison.OrdinalIgnoreCase))
                        || (_side == PlayerSide.Dire && team.Equals("Radiant", StringComparison.OrdinalIgnoreCase));
            if (!isEnemy) return;
            // Естімейт respawn timer: lvl ≈ min(30, 2 + min/1) → respawn ≈ min(80, 12 + 4*lvl).
            // Для V1.5 беремо консервативну середню: 35s. Достатньо щоб придушити ghost decay.
            float respawnSec = EstimateRespawn(_gameTime);
            _presenceTracker.MarkEnemyDied(p.PlayerID, DateTime.UtcNow, respawnSec);
        }
        catch
        {
            // ignore — наступний тік виправить
        }
    }

    private static float EstimateRespawn(float gameTimeSec)
    {
        // Дуже груба евристика: lvl ≈ 2 + minutes (clamp 2..30); respawn = 12 + 4*lvl, max 80s.
        float minutes = Math.Max(0f, gameTimeSec / 60f);
        float lvl = Math.Min(30f, 2f + minutes);
        float r = 12f + 4f * lvl;
        return Math.Min(80f, Math.Max(15f, r));
    }

    /// <summary>
    /// V1.7: подія знищення вежі. GSI дає <c>Team</c> (Radiant/Dire) і <c>Location</c>
    /// (TopLane/MiddleLane/BottomLane/Base) — без tier. Tier виводимо з порядку: 1-ша
    /// знищена на лайні = T1, 2-га = T2, 3-тя = T3, Base = T4Left/T4Right (за лічильником).
    /// </summary>
    private void OnTowerDestroyed(Dota2GSI.EventMessages.TowerDestroyed evt)
    {
        try
        {
            if (evt is null) return;
            var teamStr = evt.Team.ToString();
            var team = teamStr.Equals("Radiant", StringComparison.OrdinalIgnoreCase)
                ? TowerTeam.Radiant
                : teamStr.Equals("Dire", StringComparison.OrdinalIgnoreCase)
                    ? TowerTeam.Dire
                    : (TowerTeam?)null;
            if (team is null) return;

            var loc = evt.Location.ToString();
            var key = ResolveTowerKey(team.Value, loc);
            if (key is null) return;

            _towers = _towers.WithDestroyed(team.Value, key.Value);
            _dynamicDirty = true;
            Console.WriteLine($"[HEATMAP] Tower destroyed: {team} {key}");
        }
        catch
        {
            // ignore
        }
    }

    private TowerKey? ResolveTowerKey(TowerTeam team, string location)
    {
        string lane;
        if (location.Contains("Top", StringComparison.OrdinalIgnoreCase))    lane = "Top";
        else if (location.Contains("Mid", StringComparison.OrdinalIgnoreCase)) lane = "Mid";
        else if (location.Contains("Bot", StringComparison.OrdinalIgnoreCase)) lane = "Bot";
        else if (location.Contains("Base", StringComparison.OrdinalIgnoreCase)) lane = "Base";
        else return null;

        var counterKey = (team, lane);
        _towerDestroyCount.TryGetValue(counterKey, out int n);
        n++;
        _towerDestroyCount[counterKey] = n;

        return lane switch
        {
            "Top" => n switch { 1 => TowerKey.T1Top, 2 => TowerKey.T2Top, 3 => TowerKey.T3Top, _ => null },
            "Mid" => n switch { 1 => TowerKey.T1Mid, 2 => TowerKey.T2Mid, 3 => TowerKey.T3Mid, _ => null },
            "Bot" => n switch { 1 => TowerKey.T1Bot, 2 => TowerKey.T2Bot, 3 => TowerKey.T3Bot, _ => null },
            "Base" => n switch { 1 => TowerKey.T4Left, 2 => TowerKey.T4Right, _ => null },
            _ => (TowerKey?)null,
        };
    }

    private void OnVisionFrame(VisionFrame frame)
    {
        try
        {
            // Грубе fog-щільне поле 16×16 з пиксельної маски.
            _fogField = FogDensityField.BuildCoarse(frame.FogMask);
            _dynamicDirty = true;
            EnsureRendered(frame.Profile.Width, frame.Profile.Height, frame.Profile.IsRotated180);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Рендеримо heatmap не частіше ніж раз на секунду, і тільки при зміні параметрів.</summary>
    private void EnsureRendered(int w, int h, bool rotated)
    {
        if (w <= 0 || h <= 0) return;

        bool changed =
            w != _renderedW || h != _renderedH ||
            _side != _renderedSide ||
            rotated != _renderedRotated ||
            Math.Abs(_gameTime - _renderedGameTime) > 5f ||
            float.IsNaN(_renderedGameTime) ||
            _dynamicDirty;

        if (!changed) return;
        if ((DateTime.UtcNow - _lastRender).TotalMilliseconds < 800) return;

        try
        {
            using var dbmp = DangerHeatmapRenderer.Render(
                w, h, _side, _gameTime, rotated,
                fogField: _fogField,
                heroWorld: _heroWorld,
                empirical: _empiricalField,
                presence: _presence,
                friendlyForce: _friendly,
                towers: _towers);
            using var ms = new MemoryStream();
            dbmp.Save(ms, DImageFormat.Png);
            ms.Position = 0;
            var avaBmp = new Bitmap(ms);
            // V1.7.3: crossfade між двома Image. Новий кадр є до «back», котрий плавно виходить
            // 0→1 по Opacity (DoubleTransition 600ms), старий «front» ховається 1→0. Свопаємо ролі.
            var target = _useB ? _heatmapB : _heatmapA;
            var other = _useB ? _heatmapA : _heatmapB;
            if (target is not null)
            {
                var old = target.Source as Bitmap;
                target.Source = avaBmp;
                target.Opacity = 1.0;
                old?.Dispose();
            }
            if (other is not null) other.Opacity = 0.0;
            _useB = !_useB;
            _renderedW = w;
            _renderedH = h;
            _renderedSide = _side;
            _renderedGameTime = _gameTime;
            _renderedRotated = rotated;
            _dynamicDirty = false;
            _lastRender = DateTime.UtcNow;
        }
        catch
        {
            // ignore — повторимо
        }
    }

    /// <summary>
    /// Завантажує empirical death heatmap (V1.2). Шукає файл у двох локаціях:
    ///   1. <c>data/death-heatmap-7.41c-eu-3to6k.bin</c> поряд з exe (deployment).
    ///   2. <c>../../../../data/...bin</c> з bin/Debug/net8.0 (dev-runs з repo root).
    /// При невдачі — лишаємо <c>_empiricalField=null</c>; danger буде працювати чисто
    /// на геометричній моделі.
    /// </summary>
    private void TryLoadEmpiricalField()
    {
        const string fileName = "death-heatmap-7.41c-eu-3to6k.bin";
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "data", fileName),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "data", fileName),
                Path.Combine(baseDir, "..", "..", "..", "..", "data", fileName),
            };
            foreach (var p in candidates)
            {
                var full = Path.GetFullPath(p);
                if (File.Exists(full))
                {
                    _empiricalField = EmpiricalDeathField.Load(full);
                    Console.WriteLine($"[HEATMAP] Empirical field loaded from {full} " +
                                      $"({_empiricalField.TotalDeaths} deaths)");
                    _dynamicDirty = true;
                    return;
                }
            }
            Console.WriteLine($"[HEATMAP] Empirical field {fileName} not found; using geometric model only");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HEATMAP] Failed to load empirical field: {ex.Message}");
            _empiricalField = null;
        }
    }
}
