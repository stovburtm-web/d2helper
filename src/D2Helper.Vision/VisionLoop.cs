using System.Drawing;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Versioning;

namespace D2Helper.Vision;

/// <summary>
/// Тримає loop захоплення мінімапи на ~5Hz і публікує VisionFrame у Rx-потік.
/// Якщо гра не запущена — нічого не публікує, повторно зондує кожні 2с.
///
/// Працює у власному фоновому thread, щоб не блокувати UI.
/// Дешево по CPU: 5 кадрів × ~10мс = ~5% одного core.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VisionLoop : IDisposable
{
    private readonly MinimapCapture _capture = new();
    private readonly FogMaskDetector _fogDetector = new();
    private readonly Subject<VisionFrame> _frames = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Поточний профіль калібрування. Замінюється з UI через <see cref="SetProfile"/>.</summary>
    public CalibrationProfile? Profile { get; private set; }

    /// <summary>Стрім захоплених кадрів. Sample/throttle за потребою споживача.</summary>
    public IObservable<VisionFrame> Frames => _frames.AsObservable();

    public TimeSpan TargetInterval { get; set; } = TimeSpan.FromMilliseconds(200); // 5Hz

    public void SetProfile(CalibrationProfile profile)
    {
        Profile = profile;
        profile.Save();
    }

    public void Start()
    {
        if (_loop is not null) return;
        Profile ??= CalibrationProfile.TryLoad();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loop?.Wait(500); } catch { }
        _loop = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var started = Environment.TickCount64;
            try
            {
                CaptureOnce();
            }
            catch
            {
                // ignore — наступний tick спробує знову
            }

            var elapsed = Environment.TickCount64 - started;
            var remaining = (int)(TargetInterval.TotalMilliseconds - elapsed);
            if (remaining > 0)
            {
                try { await Task.Delay(remaining, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private void CaptureOnce()
    {
        var hwnd = DotaWindowLocator.Find();
        if (hwnd == IntPtr.Zero) return;

        var profile = Profile;
        if (profile is null)
        {
            // Гра є але користувач ще не калібрував — генеруємо дефолт для поточного розміру
            // вікна Dota і запам'ятовуємо як активний профіль, щоб UI (heatmap, debug widget)
            // міг його відразу читати. Користувач замінить через SetProfile() з калібрування.
            var client = DotaWindowLocator.GetClientRect();
            if (client is null) return;
            profile = CalibrationProfile.Default(client.Value.Width, client.Value.Height);
            Profile = profile;
        }

        var roiRect = new Rectangle(profile.Left, profile.Top, profile.Width, profile.Height);
        using var roi = _capture.CaptureRoi(hwnd, roiRect);
        if (roi is null) return;

        var fog = _fogDetector.Detect(roi);
        var fogPct = ComputeFogPercent(fog);

        // Клон ROI для UI (Bitmap не thread-safe між captures, тому копія).
        var roiCopy = new Bitmap(roi);
        _frames.OnNext(new VisionFrame(
            CapturedAtUtc: DateTime.UtcNow,
            Roi: roiCopy,
            FogMask: fog,
            FogPercent: fogPct,
            Profile: profile));
    }

    private static double ComputeFogPercent(bool[,] mask)
    {
        var w = mask.GetLength(0);
        var h = mask.GetLength(1);
        int dark = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (mask[x, y]) dark++;
        return 100.0 * dark / (w * h);
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
        _frames.OnCompleted();
    }
}

/// <summary>Знімок мінімапи з результатом первинної обробки.</summary>
[SupportedOSPlatform("windows")]
public sealed record VisionFrame(
    DateTime CapturedAtUtc,
    Bitmap Roi,
    bool[,] FogMask,
    double FogPercent,
    CalibrationProfile Profile);
