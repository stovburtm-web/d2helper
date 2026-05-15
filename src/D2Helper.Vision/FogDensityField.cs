using System.Runtime.Versioning;

namespace D2Helper.Vision;

/// <summary>
/// Будує грубе поле fog-щільності з піксельної маски та робить білінійний sampling.
/// Мета: позбутись піксельного шуму fog-detector'а — на heatmap нам потрібна
/// просторово-неперервна "де темно vs світло на мінімапі" функція.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FogDensityField
{
    /// <summary>
    /// Бере bool-маску [w,h] і повертає float-сітку [gridW, gridH], де кожне значення —
    /// частка fog-пікселів у відповідному прямокутнику.
    /// </summary>
    public static float[,] BuildCoarse(bool[,] mask, int gridW = 16, int gridH = 16)
    {
        var srcW = mask.GetLength(0);
        var srcH = mask.GetLength(1);
        var field = new float[gridW, gridH];
        if (srcW == 0 || srcH == 0) return field;

        for (int gy = 0; gy < gridH; gy++)
        {
            int y0 = gy * srcH / gridH;
            int y1 = (gy + 1) * srcH / gridH;
            if (y1 <= y0) y1 = y0 + 1;
            for (int gx = 0; gx < gridW; gx++)
            {
                int x0 = gx * srcW / gridW;
                int x1 = (gx + 1) * srcW / gridW;
                if (x1 <= x0) x1 = x0 + 1;
                int dark = 0, total = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        total++;
                        if (mask[x, y]) dark++;
                    }
                field[gx, gy] = total > 0 ? (float)dark / total : 0f;
            }
        }
        return field;
    }

    /// <summary>
    /// Bilinear sampling поля в pixel-координатах джерела (0..srcW, 0..srcH).
    /// </summary>
    public static float Sample(float[,] field, float px, float py, int srcW, int srcH)
    {
        var gw = field.GetLength(0);
        var gh = field.GetLength(1);
        if (gw == 0 || gh == 0 || srcW <= 0 || srcH <= 0) return 0f;

        // Переводимо у grid-coords (центри клітин на 0.5).
        float fx = px / srcW * gw - 0.5f;
        float fy = py / srcH * gh - 0.5f;
        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        float tx = fx - x0;
        float ty = fy - y0;

        int x1 = x0 + 1;
        int y1 = y0 + 1;
        x0 = Math.Clamp(x0, 0, gw - 1);
        x1 = Math.Clamp(x1, 0, gw - 1);
        y0 = Math.Clamp(y0, 0, gh - 1);
        y1 = Math.Clamp(y1, 0, gh - 1);

        float a = field[x0, y0];
        float b = field[x1, y0];
        float c = field[x0, y1];
        float d = field[x1, y1];
        float ab = a + (b - a) * tx;
        float cd = c + (d - c) * tx;
        return ab + (cd - ab) * ty;
    }
}
