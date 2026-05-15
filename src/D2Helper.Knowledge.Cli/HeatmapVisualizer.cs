using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using D2Helper.Vision;

namespace D2Helper.Knowledge.Cli;

/// <summary>
/// QA-утиліта: бере зібраний D2HM .bin і генерує PNG-візуалізації
/// кожного (side, time-bin) зрізу у data/heatmap-vis/.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HeatmapVisualizer
{
    public static int Run(string binPath)
    {
        if (!File.Exists(binPath))
        {
            Console.Error.WriteLine($"[viz] file not found: {binPath}");
            return 1;
        }
        var field = EmpiricalDeathField.Load(binPath);
        Console.WriteLine($"[viz] loaded {binPath}: grid={field.GridSize} bins={field.TimeBins} sides={field.Sides} totalDeaths={field.TotalDeaths}");

        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(binPath))!, "heatmap-vis");
        Directory.CreateDirectory(dir);

        string[] sideName = ["radiant", "dire"];
        string[] binName = ["0-5min", "5-10min", "10-15min", "15-25min", "25min+"];
        const int scale = 6; // 128 → 768 px

        for (int s = 0; s < field.Sides && s < sideName.Length; s++)
        {
            for (int t = 0; t < field.TimeBins && t < binName.Length; t++)
            {
                var bmp = RenderSlice(field, s, t, scale);
                var path = Path.Combine(dir, $"side-{sideName[s]}_bin-{t}-{binName[t]}.png");
                bmp.Save(path, ImageFormat.Png);
                bmp.Dispose();
            }
        }
        Console.WriteLine($"[viz] wrote {field.Sides * field.TimeBins} PNGs to {dir}");
        return 0;
    }

    /// <summary>Рендерить один (side, bin) зріз як heatmap PNG.</summary>
    private static Bitmap RenderSlice(EmpiricalDeathField field, int side, int bin, int scale)
    {
        int n = field.GridSize;
        int w = n * scale;
        // Для рендеру нам треба прямий доступ до counts → reuse Sample через
        // безпечні world-coords. world_x = (gridX - n/2) * CellWorldSize.
        // gameTime: середина bin'а (нам потрібен bin для правильного нормалайзера).
        float[] timeForBin = [120f, 450f, 750f, 1200f, 1800f];
        float gt = timeForBin[Math.Min(bin, timeForBin.Length - 1)];
        var s = side == 0 ? PlayerSide.Radiant : PlayerSide.Dire;

        var bmp = new Bitmap(w, w, PixelFormat.Format32bppArgb);
        for (int py = 0; py < w; py++)
        {
            int gy = py / scale;
            // y у файлі: 0 = top (cell-y=64). У grid'і Dota world Y зростає від Dire до Radiant,
            // тому показуємо так як є — як screen-coords (y growth = вниз). Радіант → bottom-left.
            float wy = (gy - n / 2f) * EmpiricalDeathField.CellWorldSize;
            for (int px = 0; px < w; px++)
            {
                int gx = px / scale;
                float wx = (gx - n / 2f) * EmpiricalDeathField.CellWorldSize;
                float v = field.Sample(wx, wy, s, gt);
                var color = HeatColor(v);
                bmp.SetPixel(px, py, color);
            }
        }
        return bmp;
    }

    /// <summary>Blue → green → yellow → red gradient.</summary>
    private static Color HeatColor(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        if (v < 0.01f) return Color.FromArgb(255, 20, 20, 30); // майже чорний для empty
        // 0 → blue, 0.33 → green, 0.66 → yellow, 1 → red
        int r, g, b;
        if (v < 0.33f)
        {
            float k = v / 0.33f;
            r = 0; g = (int)(255 * k); b = (int)(255 * (1 - k));
        }
        else if (v < 0.66f)
        {
            float k = (v - 0.33f) / 0.33f;
            r = (int)(255 * k); g = 255; b = 0;
        }
        else
        {
            float k = (v - 0.66f) / 0.34f;
            r = 255; g = (int)(255 * (1 - k)); b = 0;
        }
        return Color.FromArgb(255, r, g, b);
    }
}
