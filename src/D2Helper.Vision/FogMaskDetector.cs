using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace D2Helper.Vision;

/// <summary>
/// Детектор туману війни на мінімапі. Працює тривіально, але ефективно:
/// конвертує піксель в luma (яскравість), та все нижче порогу — fog.
///
/// Чому це працює: у Dota fog-of-war на мінімапі — суцільний темно-сірий,
/// видима зона — насичений колір (зелений ліс/коричнева земля/синя річка),
/// будь-який з цих кольорів має luma помітно вищу за fog.
///
/// Поріг налаштовується (`LumaThreshold`) — типове значення 60..80 для дефолтного гамма-профілю.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FogMaskDetector
{
    /// <summary>0..255. Все нижче — fog.</summary>
    public byte LumaThreshold { get; set; } = 70;

    /// <summary>
    /// Будує бітову маску того ж розміру, що й ROI, де `true` означає "fog" (темний піксель).
    /// </summary>
    public bool[,] Detect(Bitmap roi)
    {
        if (roi.PixelFormat != PixelFormat.Format32bppArgb)
            throw new ArgumentException("ROI must be 32bppArgb", nameof(roi));

        var w = roi.Width;
        var h = roi.Height;
        var mask = new bool[w, h];
        var data = roi.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var stride = data.Stride;
                var scan0 = (byte*)data.Scan0;
                var threshold = LumaThreshold;
                for (int y = 0; y < h; y++)
                {
                    var row = scan0 + y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        // BGRA: 0=B, 1=G, 2=R
                        byte b = row[x * 4 + 0];
                        byte g = row[x * 4 + 1];
                        byte r = row[x * 4 + 2];
                        // Rec.601 luma: 0.299R + 0.587G + 0.114B (за integer-формою для швидкості)
                        int luma = (r * 77 + g * 150 + b * 29) >> 8;
                        mask[x, y] = luma < threshold;
                    }
                }
            }
        }
        finally
        {
            roi.UnlockBits(data);
        }
        return mask;
    }

    /// <summary>
    /// Створює напівпрозорий червоний overlay з маски (для відображення).
    /// Прозорий де visible, червоний де fog.
    /// </summary>
    public Bitmap RenderOverlay(bool[,] fogMask, byte alpha = 110)
    {
        var w = fogMask.GetLength(0);
        var h = fogMask.GetLength(1);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var stride = data.Stride;
                var scan0 = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    var row = scan0 + y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        if (fogMask[x, y])
                        {
                            row[x * 4 + 0] = 30;        // B
                            row[x * 4 + 1] = 30;        // G
                            row[x * 4 + 2] = 220;       // R — червоний
                            row[x * 4 + 3] = alpha;     // A
                        }
                        else
                        {
                            row[x * 4 + 0] = 0;
                            row[x * 4 + 1] = 0;
                            row[x * 4 + 2] = 0;
                            row[x * 4 + 3] = 0;         // прозоро
                        }
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}
