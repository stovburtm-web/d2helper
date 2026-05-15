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

    [Fact]
    public void EarlyGame_RiverAdjacent_RadiantSide_IsContested_NotSafe()
    {
        // V1.5.2: на 0..2 хв підступи до річки на власній стороні (~(-1500, 500)..(500, -1500))
        // мають бути в "жовтій" зоні (>=0.38), бо туди ходять смок-ганки і рунотайм 2:00/4:00.
        // Раніше через сильний негативний phase-shift вони фарбувались зеленим.
        var spots = new[]
        {
            (-1500f,   500f), // ріка біля Radiant offlane
            (  500f, -1500f), // ріка біля Radiant safelane
            (   0f,     0f), // мід-river centre
        };
        foreach (var (x, y) in spots)
        {
            var d = DangerZoneModel.ComputeDanger(x, y, PlayerSide.Radiant, gameTime: 60);
            Assert.True(d >= 0.38f, $"({x},{y}) at 60s expected >=0.38 (yellow), got {d:F3}");
        }
    }

    [Fact]
    public void EarlyGame_OwnJungle_StillSafe()
    {
        // Своя сторона глибокого джунгля має лишатися зеленою на старті.
        var d = DangerZoneModel.ComputeDanger(-4000, -3000, PlayerSide.Radiant, gameTime: 60);
        Assert.True(d < 0.38f, $"deep own jungle expected <0.38 (green), got {d:F3}");
    }

    [Fact]
    public void AbsenceCrush_DoesNotMakeEnemyTerritorySafe()
    {
        // V1.5.2 regression guard: absence (всі вороги passive в фонтані → absence≈1
        // на всій мапі) НЕ має знижувати danger у ворожій зоні. Інакше вся карта зелена
        // після horn'у на 0:00.
        // Ворожий T2 mid (~2500,2500) — geometric base сильно червоний.
        var withAbsence = DangerZoneModel.ComputeDangerDynamic(
            wx: 2500, wy: 2500, side: PlayerSide.Radiant, gameTime: 60,
            absenceScore: 1f);
        Assert.True(withAbsence > 0.60f,
            $"enemy T2 with absence=1 should still be red (>0.60), got {withAbsence:F3}");

        // А свій jungle при absence=1 — навпаки має знизитись (це фіча, не баг).
        var ownJungleSafe = DangerZoneModel.ComputeDangerDynamic(
            wx: -3500, wy: -2500, side: PlayerSide.Radiant, gameTime: 600,
            absenceScore: 1f);
        var ownJungleBase = DangerZoneModel.ComputeDanger(-3500, -2500, PlayerSide.Radiant, 600);
        Assert.True(ownJungleSafe < ownJungleBase,
            $"own jungle with absence=1 should drop below base ({ownJungleBase:F3}), got {ownJungleSafe:F3}");
    }
}
