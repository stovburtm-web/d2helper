using System.Buffers.Binary;

namespace D2Helper.Vision;

/// <summary>
/// Empirical death heatmap зібрана з parsed OpenDota replays.
/// Формат файла генерує <c>D2Helper.Knowledge.Cli</c> (див. <c>DeathHeatmapAggregator.Serialize</c>):
///   magic "D2HM" (4) | version uint16 | gridSize uint16 | timeBins, sides (uint8×2)
///   | totalDeaths uint32 | uint16[side, bin, y, x] counts.
///
/// Координати у файлі = OpenDota cell - 64. Для перетворення world → grid:
///   gridX = world_x / CellWorldSize + GridSize/2
/// де CellWorldSize ≈ 128 game units, GridSize=128.
///
/// Sample повертає <b>нормалізовану</b> щільність [0..1] де 1.0 = найгарячіший cell у цьому
/// (side, time-bin)-зрізі. Це робить дані порівнянними між ранніми (мало смертей) і пізніми
/// (багато смертей) фазами гри.
/// </summary>
public sealed class EmpiricalDeathField
{
    public const int CellOffset = 64;        // OpenDota cell 64 = край мапи
    public const float CellWorldSize = 128f; // game units per cell

    public int GridSize { get; }
    public int TimeBins { get; }
    public int Sides { get; }
    public int TotalDeaths { get; }

    // [side, bin, y, x] — raw counts.
    private readonly ushort[,,,] _counts;
    // [side, bin] — max value in that slice, для нормалізації.
    private readonly float[,] _binMax;

    private EmpiricalDeathField(int gridSize, int timeBins, int sides, int totalDeaths,
                                 ushort[,,,] counts)
    {
        GridSize = gridSize;
        TimeBins = timeBins;
        Sides = sides;
        TotalDeaths = totalDeaths;
        _counts = counts;
        _binMax = new float[sides, timeBins];
        for (int s = 0; s < sides; s++)
        for (int t = 0; t < timeBins; t++)
        {
            ushort max = 0;
            for (int y = 0; y < gridSize; y++)
            for (int x = 0; x < gridSize; x++)
                if (counts[s, t, y, x] > max) max = counts[s, t, y, x];
            _binMax[s, t] = max == 0 ? 1f : max;
        }
    }

    /// <summary>Завантажує empirical heatmap з .bin файлу.</summary>
    public static EmpiricalDeathField Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Parse(bytes);
    }

    /// <summary>Парсить bin payload (для тестів).</summary>
    public static EmpiricalDeathField Parse(byte[] bytes)
    {
        if (bytes.Length < 14 || bytes[0] != (byte)'D' || bytes[1] != (byte)'2' ||
            bytes[2] != (byte)'H' || bytes[3] != (byte)'M')
            throw new InvalidDataException("not a D2HM file");

        var span = bytes.AsSpan();
        var version = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4, 2));
        if (version != 1) throw new InvalidDataException($"unsupported D2HM version {version}");
        var gridSize = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6, 2));
        int timeBins = bytes[8];
        int sides = bytes[9];
        int totalDeaths = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(10, 4));

        var counts = new ushort[sides, timeBins, gridSize, gridSize];
        int off = 14;
        for (int s = 0; s < sides; s++)
        for (int t = 0; t < timeBins; t++)
        for (int y = 0; y < gridSize; y++)
        for (int x = 0; x < gridSize; x++)
        {
            counts[s, t, y, x] = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off, 2));
            off += 2;
        }
        return new EmpiricalDeathField(gridSize, timeBins, sides, totalDeaths, counts);
    }

    /// <summary>
    /// Семплує empirical density для світової точки. Враховує сторону гравця
    /// (для Radiant беремо density саме Radiant-смертей — "де МЕНІ небезпечно").
    /// Повертає 0..1 (1 = найгарячіший cell у цьому time-bin).
    /// </summary>
    /// <param name="wx">World X (-8192..+8192).</param>
    /// <param name="wy">World Y.</param>
    /// <param name="side">Сторона гравця.</param>
    /// <param name="gameTime">Час гри, сек.</param>
    public float Sample(float wx, float wy, PlayerSide side, float gameTime)
    {
        int sIdx = side == PlayerSide.Radiant ? 0 : 1;
        if (sIdx >= Sides) return 0f;
        int bin = TimeBinForSeconds(gameTime, TimeBins);

        // world → grid coords (float для bilinear).
        float gx = wx / CellWorldSize + GridSize / 2f;
        float gy = wy / CellWorldSize + GridSize / 2f;
        if (gx < 0f || gx > GridSize - 1f || gy < 0f || gy > GridSize - 1f) return 0f;

        int x0 = (int)gx;
        int y0 = (int)gy;
        int x1 = Math.Min(x0 + 1, GridSize - 1);
        int y1 = Math.Min(y0 + 1, GridSize - 1);
        float fx = gx - x0;
        float fy = gy - y0;

        float c00 = _counts[sIdx, bin, y0, x0];
        float c10 = _counts[sIdx, bin, y0, x1];
        float c01 = _counts[sIdx, bin, y1, x0];
        float c11 = _counts[sIdx, bin, y1, x1];
        float top = c00 * (1 - fx) + c10 * fx;
        float bot = c01 * (1 - fx) + c11 * fx;
        float v = top * (1 - fy) + bot * fy;

        float max = _binMax[sIdx, bin];
        // log-stretch робить sparse rare points видимими. log(1+v)/log(1+max).
        return MathF.Log(1f + v) / MathF.Log(1f + max);
    }

    /// <summary>Маппінг секунд → bin index (синхронно з aggregator'ом).</summary>
    public static int TimeBinForSeconds(float seconds, int binCount)
    {
        int bin = seconds switch
        {
            < 300f => 0,
            < 600f => 1,
            < 900f => 2,
            < 1500f => 3,
            _ => 4,
        };
        if (bin >= binCount) bin = binCount - 1;
        return bin;
    }
}
