using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using D2Helper.Core.Models;

namespace D2Helper.Vision;

/// <summary>
/// Рендерить danger-heatmap у вигляді 32bppArgb-бітмапу заданого розміру.
/// Працює на низькому base-rate (~1Hz достатньо, бо модель змінюється тільки з gameTime).
/// </summary>
[SupportedOSPlatform("windows")]
public static class DangerHeatmapRenderer
{
    /// <summary>
    /// Малює прозорий тепло-шар.
    /// </summary>
    /// <param name="width">Ширина бітмапу в px (зазвичай = minimap width на екрані).</param>
    /// <param name="height">Висота бітмапу в px.</param>
    /// <param name="side">Сторона гравця.</param>
    /// <param name="gameTime">Час гри, сек.</param>
    /// <param name="isRotated180">true якщо мінімапа візуально перевернута (Dire з опцією).</param>
    /// <param name="alpha">Непрозорість шару (0..255). Дефолт ~90 ≈ 35%.</param>
    public static unsafe Bitmap Render(int width, int height, PlayerSide side, float gameTime,
                                       bool isRotated180, byte alpha = 110,
                                       float[,]? fogField = null,
                                       (float X, float Y)? heroWorld = null,
                                       EmpiricalDeathField? empirical = null,
                                       EnemyPresenceSnapshot? presence = null,
                                       EnemyPresenceSnapshot? friendlyForce = null)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* p = (byte*)data.Scan0;
            int stride = data.Stride;

            for (int y = 0; y < height; y++)
            {
                byte* row = p + y * stride;
                for (int x = 0; x < width; x++)
                {
                    // Конвертуємо pixel-coord у world-coord, з урахуванням можливого 180°-rotation.
                    var (wx, wy) = WorldToMinimap.ToWorld(x, y, width, height, isRotated180);

                    // Fog density для цього пікселя (0..1, 0.5 = нейтрально).
                    float fog = 0.5f;
                    if (fogField is not null)
                        fog = FogDensityField.Sample(fogField, x, y, width, height);

                    float? hx = heroWorld?.X;
                    float? hy = heroWorld?.Y;
                    float emp = empirical is null
                        ? float.NaN
                        : empirical.Sample(wx, wy, side, gameTime);
                    float pres = presence is null ? float.NaN : presence.SampleLocal(wx, wy);
                    float absc = presence is null ? float.NaN : presence.SampleAbsence(wx, wy);
                    float fc = friendlyForce is null ? float.NaN : friendlyForce.SampleLocal(wx, wy);
                    float danger = DangerZoneModel.ComputeDangerDynamic(
                        wx, wy, side, gameTime,
                        fogDensity: fog,
                        heroX: hx, heroY: hy,
                        empiricalDensity: emp,
                        presenceLocal: pres,
                        absenceScore: absc,
                        friendlyControl: fc);

                    // 3 чіткі зони з різкими межами. Жовтий — вузька contested-смуга.
                    var (r, g, b, a) = DangerToBand(danger, alpha);

                    // V1.4.1: vignette по периметру — щоб не було видно квадратних
                    // границь нашого оверлею на тлі круглуватої мінімапи Dota.
                    // Внутрішні ~78% радіуса — повна непрозорість, далі плавне згасання.
                    float nx = (x / (float)(width - 1)) * 2f - 1f;   // [-1..1]
                    float ny = (y / (float)(height - 1)) * 2f - 1f;
                    // Квадратна норма (chebyshev): краще збігається з прямокутною мінімапою.
                    float rEdge = Math.Max(Math.Abs(nx), Math.Abs(ny));
                    float fade;
                    if (rEdge <= 0.78f) fade = 1f;
                    else if (rEdge >= 0.98f) fade = 0f;
                    else
                    {
                        float t = (rEdge - 0.78f) / (0.98f - 0.78f);
                        // smoothstep: 3t² - 2t³
                        fade = 1f - t * t * (3f - 2f * t);
                    }
                    a = (byte)(a * fade);

                    byte* px = row + x * 4;
                    // 32bppArgb у GDI+ записується як BGRA (little-endian).
                    px[0] = b;
                    px[1] = g;
                    px[2] = r;
                    px[3] = a;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    /// <summary>3 дискретні зони з різкими межами:
    /// <list type="bullet">
    /// <item>danger &lt; 0.38 → <b>safe</b> (зелений)</item>
    /// <item>0.38 ≤ danger ≤ 0.60 → <b>contested</b> (жовтий, тонша смуга)</item>
    /// <item>danger &gt; 0.60 → <b>danger</b> (червоний)</item>
    /// </list>
    /// Жовта середина має нижчу альфу, щоб не закривала весь центр мапи.</summary>
    private static (byte R, byte G, byte B, byte A) DangerToBand(float d, byte baseAlpha)
    {
        if (d < 0.38f)
            return (40, 200, 80, baseAlpha);          // safe — зелений
        if (d > 0.60f)
            return (230, 40, 30, baseAlpha);          // danger — червоний
        // contested — жовто-помаранчевий, легша заливка
        byte mid = (byte)(baseAlpha * 0.55f);
        return (240, 200, 40, mid);
    }

    private static int Lerp(int a, int b, float t) => (int)(a + (b - a) * t);
}
