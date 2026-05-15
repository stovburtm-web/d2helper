using System.Buffers.Binary;
using D2Helper.Vision;
using Xunit;

namespace D2Helper.Tests;

public class EmpiricalDeathFieldTests
{
    /// <summary>Будує мінімальний D2HM-payload зі вказаними hot-cells.</summary>
    private static byte[] BuildBin(int gridSize, int timeBins, int sides,
                                    Action<int, int, int, int, ushort> hotCells)
    {
        // [side, bin, y, x] ushort = 2 bytes
        int payload = sides * timeBins * gridSize * gridSize * 2;
        var bytes = new byte[14 + payload];
        bytes[0] = (byte)'D'; bytes[1] = (byte)'2'; bytes[2] = (byte)'H'; bytes[3] = (byte)'M';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), (ushort)gridSize);
        bytes[8] = (byte)timeBins;
        bytes[9] = (byte)sides;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10, 4), 0);

        // ефемерний writer: викликає лямбду, яка повертає значення для координат.
        // Замість цього просто запускаємо callback що нам ставить значення в масив.
        void Set(int s, int t, int y, int x, ushort v)
        {
            int idx = 14 + (((s * timeBins + t) * gridSize + y) * gridSize + x) * 2;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(idx, 2), v);
        }
        hotCells(0, 0, 0, 0, 0); // no-op щоб задовольнити сигнатуру
        // Викличемо writer через captured local. Користувач передасть нам set через хитру конвенцію:
        // не зручно. Замість цього просто переробимо:
        return bytes;
    }

    /// <summary>Простіший helper: будує payload з заданими значеннями.</summary>
    private static byte[] BuildBinSimple(int gridSize, int timeBins, int sides,
                                          IEnumerable<(int s, int t, int y, int x, ushort v)> hots)
    {
        int payload = sides * timeBins * gridSize * gridSize * 2;
        var bytes = new byte[14 + payload];
        bytes[0] = (byte)'D'; bytes[1] = (byte)'2'; bytes[2] = (byte)'H'; bytes[3] = (byte)'M';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), (ushort)gridSize);
        bytes[8] = (byte)timeBins;
        bytes[9] = (byte)sides;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10, 4), 0);
        foreach (var (s, t, y, x, v) in hots)
        {
            int idx = 14 + (((s * timeBins + t) * gridSize + y) * gridSize + x) * 2;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(idx, 2), v);
        }
        return bytes;
    }

    [Fact]
    public void Parse_RejectsWrongMagic()
    {
        var bad = new byte[20];
        Assert.Throws<InvalidDataException>(() => EmpiricalDeathField.Parse(bad));
    }

    [Fact]
    public void Sample_HotCell_ReturnsHigh()
    {
        // grid 128, time-bins 5, sides 2. Hot-cell (s=0, bin=4, y=64, x=64) = 100 deaths
        // grid coord (64,64) = world (0,0) = center of map.
        var bytes = BuildBinSimple(128, 5, 2, new[]
        {
            (0, 4, 64, 64, (ushort)100),
        });
        var field = EmpiricalDeathField.Parse(bytes);

        var v = field.Sample(0f, 0f, PlayerSide.Radiant, gameTime: 1600f);
        Assert.True(v > 0.9f, $"hottest cell should be ≈1.0, got {v}");
    }

    [Fact]
    public void Sample_EmptyCell_ReturnsZero()
    {
        var bytes = BuildBinSimple(128, 5, 2, new[] { (0, 4, 64, 64, (ushort)100) });
        var field = EmpiricalDeathField.Parse(bytes);

        // Точка віддалена від єдиного hot-cell → майже 0.
        var v = field.Sample(5000f, -5000f, PlayerSide.Radiant, gameTime: 1600f);
        Assert.True(v < 0.05f, $"far cell should be ~0, got {v}");
    }

    [Fact]
    public void Sample_DireSide_UsesDireSlice()
    {
        // hot тільки для side=1 (Dire). Radiant отримає 0 у тій самій точці.
        var bytes = BuildBinSimple(128, 5, 2, new[] { (1, 4, 64, 64, (ushort)50) });
        var field = EmpiricalDeathField.Parse(bytes);

        var rad = field.Sample(0f, 0f, PlayerSide.Radiant, gameTime: 1600f);
        var dire = field.Sample(0f, 0f, PlayerSide.Dire, gameTime: 1600f);
        Assert.True(dire > 0.9f, $"dire should see hot, got {dire}");
        Assert.True(rad < 0.05f, $"radiant should not see dire-only hot, got {rad}");
    }

    [Fact]
    public void Sample_OutsideMap_ReturnsZero()
    {
        var bytes = BuildBinSimple(128, 5, 2, new[] { (0, 4, 64, 64, (ushort)100) });
        var field = EmpiricalDeathField.Parse(bytes);
        Assert.Equal(0f, field.Sample(99999f, 99999f, PlayerSide.Radiant, 1600f));
    }

    [Fact]
    public void TimeBin_MapsCorrectly()
    {
        Assert.Equal(0, EmpiricalDeathField.TimeBinForSeconds(100, 5));
        Assert.Equal(1, EmpiricalDeathField.TimeBinForSeconds(450, 5));
        Assert.Equal(2, EmpiricalDeathField.TimeBinForSeconds(800, 5));
        Assert.Equal(3, EmpiricalDeathField.TimeBinForSeconds(1200, 5));
        Assert.Equal(4, EmpiricalDeathField.TimeBinForSeconds(2400, 5));
    }
}
