using System.Buffers.Binary;

namespace D2Helper.Knowledge.Cli;

/// <summary>
/// Агрегатор смертей з parsed-replays. Видає grid <c>[side, timeBin, y, x]</c> кількостей смертей.
///
/// Координатна система — як у OpenDota <c>deaths_pos</c>: 128×128 cell-grid, де cell 64..192
/// покриває world -8192..+8192. Тобто <c>world_x = (cell - 128) * 128</c>.
///
/// Time-bins (5 штук): 0-5хв, 5-10, 10-15, 15-25, 25+. Без розбивки по ролях (за рішенням
/// користувача — у нас heatmap "де гравці взагалі вмирають", а не "де вмирає саппорт").
///
/// Розділ Radiant/Dire потрібен, бо смерті Radiant-команди корисні для гравця-Radiant
/// (показує "де МИ вмираємо" → це для нас danger). Дзеркально для Dire.
/// </summary>
public sealed class DeathHeatmapAggregator
{
    /// <summary>
    /// Розмір cell-grid'у. OpenDota deaths_pos має cells у діапазоні ~64..192,
    /// тобто 128-cell-широка область покриває всю мапу. Перед запам'ятовуванням
    /// віднімаємо <see cref="CellOffset"/> щоб мапнути 64..191 → 0..127.
    /// </summary>
    public const int GridSize = 128;

    /// <summary>OpenDota cell 64 = край мапи. Зсуваємо до 0.</summary>
    public const int CellOffset = 64;

    public const int TimeBins = 5;
    public const int Sides = 2; // 0=Radiant, 1=Dire

    // [side, timeBin, y, x]
    private readonly int[,,,] _counts = new int[Sides, TimeBins, GridSize, GridSize];

    public int TotalDeaths { get; private set; }

    /// <summary>Записує одну смерть у grid.</summary>
    /// <param name="cellX">RAW OpenDota cell X (~64..192). Внутрішньо зсунеться на -64.</param>
    /// <param name="cellY">RAW OpenDota cell Y (~64..192).</param>
    /// <param name="timeSeconds">Час від horn (gameTime).</param>
    /// <param name="isRadiant">true якщо жертва — Radiant.</param>
    public void Add(int cellX, int cellY, int timeSeconds, bool isRadiant)
    {
        int gx = cellX - CellOffset;
        int gy = cellY - CellOffset;
        if (gx < 0 || gx >= GridSize || gy < 0 || gy >= GridSize) return;
        var bin = TimeBin(timeSeconds);
        var side = isRadiant ? 0 : 1;
        _counts[side, bin, gy, gx]++;
        TotalDeaths++;
    }

    public static int TimeBin(int seconds) => seconds switch
    {
        < 300 => 0,
        < 600 => 1,
        < 900 => 2,
        < 1500 => 3,
        _ => 4,
    };

    /// <summary>
    /// Серіалізує grid у компактний бінарний формат:
    ///   - magic "D2HM" (4)
    ///   - version uint16 = 1
    ///   - gridSize uint16
    ///   - timeBins, sides — обидва uint8
    ///   - totalDeaths uint32
    ///   - дані: [side][bin][y][x] як <c>uint16</c> (clamp 65535) — порядок little-endian.
    /// Розмір файла ≈ 2 × 5 × 128 × 128 × 2 = 320kB.
    /// </summary>
    public byte[] Serialize()
    {
        var header = new byte[4 + 2 + 2 + 1 + 1 + 4];
        header[0] = (byte)'D'; header[1] = (byte)'2'; header[2] = (byte)'H'; header[3] = (byte)'M';
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), GridSize);
        header[8] = TimeBins;
        header[9] = Sides;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(10, 4), (uint)TotalDeaths);

        const int dataBytes = Sides * TimeBins * GridSize * GridSize * 2;
        var buf = new byte[header.Length + dataBytes];
        header.CopyTo(buf, 0);
        var span = buf.AsSpan(header.Length);
        int off = 0;
        for (int s = 0; s < Sides; s++)
        for (int t = 0; t < TimeBins; t++)
        for (int y = 0; y < GridSize; y++)
        for (int x = 0; x < GridSize; x++)
        {
            var v = _counts[s, t, y, x];
            if (v > ushort.MaxValue) v = ushort.MaxValue;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(off, 2), (ushort)v);
            off += 2;
        }
        return buf;
    }

    /// <summary>Швидкий звіт у консоль для перевірки розподілу.</summary>
    public string SummarizeForLogging()
    {
        var perBin = new int[TimeBins];
        var perSide = new int[Sides];
        int max = 0;
        for (int s = 0; s < Sides; s++)
        for (int t = 0; t < TimeBins; t++)
        for (int y = 0; y < GridSize; y++)
        for (int x = 0; x < GridSize; x++)
        {
            var v = _counts[s, t, y, x];
            perBin[t] += v;
            perSide[s] += v;
            if (v > max) max = v;
        }
        return
            $"  total deaths: {TotalDeaths}\n" +
            $"  by side: Radiant={perSide[0]} Dire={perSide[1]}\n" +
            $"  by bin: 0-5={perBin[0]} 5-10={perBin[1]} 10-15={perBin[2]} 15-25={perBin[3]} 25+={perBin[4]}\n" +
            $"  max-cell={max}";
    }
}
