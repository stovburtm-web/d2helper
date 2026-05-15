using D2Helper.Vision;
using Xunit;

namespace D2Helper.Tests;

public class DangerZoneModelTests
{
    [Fact]
    public void Radiant_OwnFountain_IsSafe()
    {
        var d = DangerZoneModel.ComputeDanger(-7000, -7000, PlayerSide.Radiant, gameTime: 0);
        Assert.True(d < 0.2f, $"expected safe (<0.2), got {d}");
    }

    [Fact]
    public void Radiant_EnemyFountain_IsDeadly()
    {
        var d = DangerZoneModel.ComputeDanger(7000, 7000, PlayerSide.Radiant, gameTime: 0);
        Assert.True(d > 0.8f, $"expected danger (>0.8), got {d}");
    }

    [Fact]
    public void Dire_OwnFountain_IsSafe()
    {
        var d = DangerZoneModel.ComputeDanger(7000, 7000, PlayerSide.Dire, gameTime: 0);
        Assert.True(d < 0.2f);
    }

    [Fact]
    public void Dire_EnemyFountain_IsDeadly()
    {
        var d = DangerZoneModel.ComputeDanger(-7000, -7000, PlayerSide.Dire, gameTime: 0);
        Assert.True(d > 0.8f);
    }

    [Fact]
    public void LateGame_OwnJungle_BecomesMoreDangerous()
    {
        // (-3000,-3000) — приблизно секретний магазин Radiant / межа джунглів.
        var early = DangerZoneModel.ComputeDanger(-3000, -3000, PlayerSide.Radiant, gameTime: 0);
        var late  = DangerZoneModel.ComputeDanger(-3000, -3000, PlayerSide.Radiant, gameTime: 1800);
        Assert.True(late > early, $"late ({late}) should be > early ({early})");
    }

    [Fact]
    public void Midline_Hovers_Around_Half_Early_Game()
    {
        // Центр мапи (0,0) — поблизу річки, має бути ~0.5 у ранній грі.
        var d = DangerZoneModel.ComputeDanger(0, 0, PlayerSide.Radiant, gameTime: 60);
        Assert.InRange(d, 0.30f, 0.70f);
    }
}
