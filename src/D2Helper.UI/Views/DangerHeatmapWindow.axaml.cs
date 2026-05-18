using System.IO;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using D2Helper.Core.Gsi;
using D2Helper.Core.Models;
using D2Helper.Vision;
using Dota2GSI;
using DBitmap = System.Drawing.Bitmap;
using DImageFormat = System.Drawing.Imaging.ImageFormat;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

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
    // V2.0/V2.1/V2.2: real-time FightDetector. Видає FightEvent на HP-burst, CC-edge або
    // pre-fight (кластер/ізоляція) — підсвічуємо на мінімапі та (для Teamfight) пінгаємо звук.
    private readonly FightDetector _fightDetector = new();
    private DateTime _lastBeepUtc = DateTime.MinValue;
    // V2.0: per-hero кулдаун 30с на пінг. FightDetector може фаїрити послідовно (CC → burst → death)
    // для одного в того ж героя — render-layer стискає, щоб не спамити. Key = PlayerId.
    private readonly Dictionary<int, DateTime> _lastPulseByPlayer = new();
    private const double PulsePerHeroCooldownSec = 30.0;
    // V2.0.1: синтетичний двотоновий пінг (D6→G6, ~280мс, 16-bit mono PCM @ 44.1kHz).
    // Будується раз у TryLoadTeamfightSound() і кешується тут; на кожен Play() — новий
    // RawSourceWaveStream+WaveOutEvent, щоб concurrent ping'и не глушили один одного.
    // Фолбек: Win32 MessageBeep.
    private byte[]? _pingPcmBytes;
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
    private Canvas? _pulseCanvas; // V2.0: лейер для FightEvent-пульсів поверх heatmap
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
        _pulseCanvas = this.FindControl<Canvas>("PulseCanvas");

        Opened += (_, _) =>
        {
            // Click-through застосовуємо ОДИН раз — як OverlayWindow з квестами.
            ApplyClickThroughStyle();
            // А також ПОВТОРНО через Background-prio dispatcher post — щоб виконатись
            // ПІСЛЯ усіх внутрішніх Avalonia style-mutations, які можуть скидати WS_EX.
            Dispatcher.UIThread.Post(ApplyClickThroughStyle, DispatcherPriority.Background);
            TryLoadEmpiricalField();
            TryLoadTeamfightSound();
            StartPositionLoop();
            _gsiSub = _gsi.States
                .Sample(TimeSpan.FromMilliseconds(250))
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

            // V2.0/V2.1/V2.2: feed FightDetector. Він повертає список FightEvent цього тіка
            // (death-edge, hp-burst, CC-edge, pre-fight кластер/ізоляція) — спавнимо пульси.
            try
            {
                var events = _fightDetector.Update(gs, _side == PlayerSide.Radiant, _presence, DateTime.UtcNow);
                if (events.Count > 0)
                {
                    foreach (var ev in events)
                        SpawnPulse(ev);
                }
            }
            catch
            {
                // FightDetector помилка не має ламати heatmap loop
            }

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

    // ============================================================
    // V2.0 — FightDetector pulses + audio ping
    // ============================================================

    /// <summary>
    /// Win32 MessageBeep — асинхронний beep дефолтним системним звуком. Не блокує UI потік.
    /// Тип 0x10 = MB_ICONHAND (різкий "критичний" звук) — для Teamfight.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    private const uint MB_ICONHAND = 0x00000010;

    /// <summary>
    /// V2.0.1 — синтетичний двотоновий пінг замість mp3. Чиста синусоїда D4→G4 (293.66→392Hz),
    /// загалом ~350ms з фейдами. Низько-середній регістр (як телефонний тон), теплий і впізнаваний,
    /// але в Dota такого UI-звуку немає, тому добре «вибивається» з ігрового міксу й не дратує.
    /// Будується раз і кешується в <see cref="_pingPcmBytes"/>.
    /// </summary>
    private void TryLoadTeamfightSound()
    {
        try
        {
            _pingPcmBytes = BuildTwoTonePingPcm();
            Console.WriteLine($"[HEATMAP] synthetic ping ready ({_pingPcmBytes.Length} bytes PCM)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HEATMAP] Failed to build ping: {ex.Message}");
            _pingPcmBytes = null;
        }
    }

    /// <summary>
    /// Генерує 16-bit mono 44100Hz PCM: тон1 D4 (293.66Hz) 150ms → пауза 30ms → тон2 G4 (392Hz) 170ms.
    /// Низько-середній регістр, теплий, не пискливий; чиста синусоїда не звучить як жоден звук Dota.
    /// Кожен тон має короткий attack (4ms) і експоненційний decay, щоб не клацало.
    /// </summary>
    private static byte[] BuildTwoTonePingPcm()
    {
        const int sr = 44100;
        var seg = new (double freq, int ms)[] { (293.66, 150), (0, 30), (392.00, 170) };
        int totalSamples = 0;
        foreach (var (_, ms) in seg) totalSamples += sr * ms / 1000;
        var pcm = new byte[totalSamples * 2];
        int pos = 0;
        double phase = 0;
        foreach (var (freq, ms) in seg)
        {
            int n = sr * ms / 1000;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)n;
                double env;
                if (freq <= 0)
                {
                    env = 0; // пауза
                }
                else
                {
                    // attack 4ms, decay експоненційний
                    double attack = Math.Min(1.0, i / (sr * 0.004));
                    double decay = Math.Exp(-3.0 * t);
                    env = attack * decay;
                }
                double s = freq > 0 ? Math.Sin(phase) * env * 0.85 : 0;
                short sample = (short)(s * short.MaxValue);
                pcm[pos++] = (byte)(sample & 0xff);
                pcm[pos++] = (byte)((sample >> 8) & 0xff);
                if (freq > 0) phase += 2 * Math.PI * freq / sr;
                else phase = 0;
            }
        }
        return pcm;
    }

    private void PlayTeamfightPing()
    {
        if (_pingPcmBytes is not null)
        {
            try
            {
                var ms = new MemoryStream(_pingPcmBytes);
                var fmt = new NAudio.Wave.WaveFormat(44100, 16, 1);
                var reader = new NAudio.Wave.RawSourceWaveStream(ms, fmt);
                var output = new NAudio.Wave.WaveOutEvent { Volume = 0.55f }; // 55% — чути поверх Dota, не б'є по вухах
                output.PlaybackStopped += (_, _) =>
                {
                    try { output.Dispose(); } catch { }
                    try { reader.Dispose(); } catch { }
                    try { ms.Dispose(); } catch { }
                };
                output.Init(reader);
                output.Play();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HEATMAP] NAudio play failed: {ex.Message}");
            }
        }
        try { MessageBeep(MB_ICONHAND); } catch { /* no audio device */ }
    }

    /// <summary>
    /// Спавнить пульс-коло на мінімапі для конкретного <see cref="FightEvent"/>.
    /// Колір/розмір/тривалість залежать від <see cref="FightSeverity"/>.
    /// Для Teamfight — також pingає звук (не частіше 1 разу на 2 секунди).
    /// </summary>
    private void SpawnPulse(FightEvent ev)
    {
        if (_pulseCanvas is null || _placedW <= 0 || _placedH <= 0) return;
        var profile = _vision.Profile;
        if (profile is null) return;

        bool tf = ev.Severity == FightSeverity.Teamfight;

        // V2.0: per-hero кулдаун — ТІЛЬКИ для Teamfight (циан + звук). FightDetector може
        // фаїрити CC → burst → death послідовно на одного героя; такий «червоний» спам
        // дратує і губить інформацію. Skirmish (жовтий, без звуку) лишається без cooldown'у —
        // дрібні події на лайні мають бачитись як вони є.
        // Якщо в події кілька аллі (cluster_cc) — хоча б один має бути «свіжим».
        if (tf && ev.InvolvedAllyIds is { Count: > 0 })
        {
            var now = DateTime.UtcNow;
            bool anyFresh = false;
            foreach (var pid in ev.InvolvedAllyIds)
            {
                if (!_lastPulseByPlayer.TryGetValue(pid, out var last) ||
                    (now - last).TotalSeconds >= PulsePerHeroCooldownSec)
                {
                    anyFresh = true;
                    break;
                }
            }
            if (!anyFresh) return;
            foreach (var pid in ev.InvolvedAllyIds) _lastPulseByPlayer[pid] = now;
        }

        // World → пікселі мінімапи (профіль) → DIP (ділимо на DPI scale).
        var (pxX, pxY) = WorldToMinimap.ToMinimap(ev.Wx, ev.Wy, _placedW, _placedH, profile.IsRotated180);
        double dipX = pxX / _screenScale;
        double dipY = pxY / _screenScale;

        // V2.0 кольорова палітра: вибирали щоб НЕ зливались з червоним heatmap-фоном.
        //   Teamfight = неоновий ціан (#00E5FF) — максимальний контраст до червоного.
        //   Skirmish   = насичений жовтий (#FFD600).
        // Додано чорний BoxShadow як halo — відокремлює коло від будь-якого фону.
        double maxSize = tf ? 52 : 34;
        var color = tf ? Color.FromRgb(0x00, 0xE5, 0xFF) : Color.FromRgb(0xFF, 0xD6, 0x00);
        int durMs = tf ? 4500 : 2500;

        var scale = new ScaleTransform(0.35, 0.35);
        var el = new Ellipse
        {
            Width = maxSize,
            Height = maxSize,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = tf ? 4 : 3,
            Fill = new SolidColorBrush(color, 0.22),
            Opacity = 1.0,
            IsHitTestVisible = false,
            RenderTransform = scale,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            // Чорний glow для відокремлення від червоного фону heatmap.
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 8,
                OffsetX = 0,
                OffsetY = 0,
                Opacity = 0.95
            },
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(durMs) }
            }
        };
        scale.Transitions = new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(durMs) },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(durMs) }
        };
        Canvas.SetLeft(el, dipX - maxSize / 2.0);
        Canvas.SetTop(el, dipY - maxSize / 2.0);
        _pulseCanvas.Children.Add(el);

        // Іконка в центрі (⚔ для Teamfight, ! для Skirmish) — допомагає помітити подію
        // навіть коли коло вже почало згасати. Тримається на повній opacity до останніх 500мс.
        double iconBox = tf ? 28.0 : 20.0;
        var icon = new Border
        {
            Width = iconBox,
            Height = iconBox,
            IsHitTestVisible = false,
            Opacity = 1.0,
            Child = new TextBlock
            {
                Text = tf ? "\u2694" : "!",                // ⚔ U+2694 / ASCII !
                FontSize = tf ? 22 : 18,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            },
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                OffsetX = 0,
                OffsetY = 0,
                Opacity = 1.0
            },
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(500) }
            }
        };
        Canvas.SetLeft(icon, dipX - iconBox / 2.0);
        Canvas.SetTop(icon, dipY - iconBox / 2.0);
        _pulseCanvas.Children.Add(icon);

        // Кікаємо transition на наступний UI-tick щоб Avalonia встигла зареєструвати початкові значення.
        Dispatcher.UIThread.Post(() =>
        {
            el.Opacity = 0;
            scale.ScaleX = 1.4;
            scale.ScaleY = 1.4;
        }, DispatcherPriority.Background);

        // Прибираємо ellipse + іконку після завершення анімації.
        // Іконка фейдиться лише в останні 500мс — так встигаєш помітити її очима.
        DispatcherTimer.RunOnce(() => icon.Opacity = 0, TimeSpan.FromMilliseconds(Math.Max(0, durMs - 500)));
        DispatcherTimer.RunOnce(() =>
        {
            _pulseCanvas?.Children.Remove(el);
            _pulseCanvas?.Children.Remove(icon);
        }, TimeSpan.FromMilliseconds(durMs + 100));

        if (tf)
        {
            var nowBeep = DateTime.UtcNow;
            if ((nowBeep - _lastBeepUtc).TotalMilliseconds >= 2000)
            {
                _lastBeepUtc = nowBeep;
                PlayTeamfightPing();
            }
        }

        Console.WriteLine($"[FIGHT] {ev.Severity} {ev.Reason} t={ev.EventTime:F1}s wx={ev.Wx:F0} wy={ev.Wy:F0} allies=[{string.Join(",", ev.InvolvedAllyIds)}]");
    }
}
