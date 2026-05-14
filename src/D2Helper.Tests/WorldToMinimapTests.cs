using D2Helper.Vision;
using Xunit;

namespace D2Helper.Tests;

public class WorldToMinimapTests
{
    private const int W = 256;
    private const int H = 256;

    [Theory]
    [InlineData(0f, 0f, false)]
    [InlineData(-8288f, -8288f, false)]
    [InlineData(8288f, 8288f, false)]
    [InlineData(1234f, -567f, false)]
    [InlineData(0f, 0f, true)]
    [InlineData(-8288f, -8288f, true)]
    [InlineData(8288f, 8288f, true)]
    [InlineData(1234f, -567f, true)]
    public void RoundTrip_WorldToMinimapToWorld_Recovers(float wx, float wy, bool rotated)
    {
        var (px, py) = WorldToMinimap.ToMinimap(wx, wy, W, H, rotated);
        var (rx, ry) = WorldToMinimap.ToWorld(px, py, W, H, rotated);
        Assert.InRange(rx - wx, -1f, 1f);
        Assert.InRange(ry - wy, -1f, 1f);
    }

    [Fact]
    public void RadiantBase_Bottom_Left_OfMinimap()
    {
        // Radiant fountain ~ (-7000, -7000) у world. Без розвороту — лівий-низ мінімапи.
        var (px, py) = WorldToMinimap.ToMinimap(-7000f, -7000f, W, H, isRotated180: false);
        Assert.InRange(px, 0, W * 0.25f);          // лівий чверть
        Assert.InRange(py, H * 0.75f, H);          // нижня чверть
    }

    [Fact]
    public void DireBase_Bottom_Left_When_Rotated()
    {
        // Dire fountain ~ (7000, 7000). З розворотом — теж має бути лівий-низ
        // (бо гравець за Dire бачить свою базу знизу-зліва).
        var (px, py) = WorldToMinimap.ToMinimap(7000f, 7000f, W, H, isRotated180: true);
        Assert.InRange(px, 0, W * 0.25f);
        Assert.InRange(py, H * 0.75f, H);
    }
}
