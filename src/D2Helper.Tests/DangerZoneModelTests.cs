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
    public void AbsenceCrush_WithPresenceAtTarget_KeepsEnemyTerritoryRed()
    {
        // V1.6: на ворожій highground stays red ТІЛЬКИ якщо там presenceLocal > 0
        // (вороги фізично там — фонтан defending, або push). Якщо ворогів там нема
        // (absence=1) → природно знижується. Це і є те, що бачить гравець на minimap.
        var withPresenceAtT2 = DangerZoneModel.ComputeDangerDynamic(
            wx: 2500, wy: 2500, side: PlayerSide.Radiant, gameTime: 60,
            presenceLocal: 1f, absenceScore: 0.5f);
        Assert.True(withPresenceAtT2 > 0.60f,
            $"enemy T2 with enemies actually there (presence=1) stays red, got {withPresenceAtT2:F3}");

        // А свій jungle при absence=1 — навпаки має знизитись (це фіча, не баг).
        var ownJungleSafe = DangerZoneModel.ComputeDangerDynamic(
            wx: -3500, wy: -2500, side: PlayerSide.Radiant, gameTime: 600,
            absenceScore: 1f);
        var ownJungleBase = DangerZoneModel.ComputeDanger(-3500, -2500, PlayerSide.Radiant, 600);
        Assert.True(ownJungleSafe < ownJungleBase,
            $"own jungle with absence=1 should drop below base ({ownJungleBase:F3}), got {ownJungleSafe:F3}");
    }

    [Fact]
    public void EarlyGame_EnemyTowerZone_StaysAtLeastYellow_DespiteAbsence()
    {
        // V1.6.1: на 0..6хв ворожі вежі стоять. Навіть якщо всі вороги купкою у фонтані
        // (absence≈1 на T1 bot), зона ворожого T1 НЕ має падати в зелене (< 0.38).
        // Tower aggression — фізична загроза, не залежна від присутності героїв.
        var enemyT1BotForDire = DangerZoneModel.ComputeDangerDynamic(
            wx: 5000, wy: -6000, side: PlayerSide.Dire, gameTime: 1f,
            absenceScore: 1f);
        Assert.True(enemyT1BotForDire >= 0.38f,
            $"Dire's view of Radiant T1 bot at 0:01 should stay yellow+ even with absence=1, got {enemyT1BotForDire:F3}");

        // Pure base без presence — для порівняння (тоді що floor працює).
        var pureBase = DangerZoneModel.ComputeDanger(5000, -6000, PlayerSide.Dire, 1f);
        Assert.True(enemyT1BotForDire >= pureBase * 0.75f,
            $"floor should be ≥75% of base on early game, base={pureBase:F3}, got={enemyT1BotForDire:F3}");
    }

    [Fact]
    public void LateGame_DeepPush_AbsenceCanStillCrushDeepEnemyZone()
    {
        // На late game (25+хв) при confirmed absence (5-ка пушить мід — bot deep пустий)
        // floor слабкий → можна показати "відносно безпечно" в bot Dire jungle коли там
        // справді нікого. Yellow допустимо, але не deep red.
        var lateGameAbsent = DangerZoneModel.ComputeDangerDynamic(
            wx: 5000, wy: -3000, side: PlayerSide.Radiant, gameTime: 1800f,
            absenceScore: 1f);
        var lateGameBase = DangerZoneModel.ComputeDanger(5000, -3000, PlayerSide.Radiant, 1800f);
        Assert.True(lateGameAbsent < lateGameBase * 0.6f,
            $"late game absence should crush significantly below base ({lateGameBase:F3}), got {lateGameAbsent:F3}");
    }

    // === V1.7: tower aura ===

    [Fact]
    public void V17_AliveEnemyTower_AddsDanger_OverAbsence()
    {
        // Radiant гравець, на 0:01 (всі вежі живі), absence=1 (5-ка ворогів у фонтані).
        // Без tower aura — danger у ворожому Т1 bot crushed до floor ≈ 0.41 (V1.6.1).
        // З tower aura — Dire T1 bot жива → додає сильний +danger → точка ≥0.55 (red/orange).
        var towers = D2Helper.Core.Models.TowerSnapshot.AllAlive();
        var (tx, ty) = D2Helper.Core.Models.TowerMap.Dire[D2Helper.Core.Models.TowerKey.T1Bot];
        float aura = towers.SampleAura(tx, ty, playerIsRadiant: true);

        var withTower = DangerZoneModel.ComputeDangerDynamic(
            wx: tx, wy: ty, side: PlayerSide.Radiant, gameTime: 1f,
            absenceScore: 1f, towerAuraLocal: aura);
        var withoutTower = DangerZoneModel.ComputeDangerDynamic(
            wx: tx, wy: ty, side: PlayerSide.Radiant, gameTime: 1f,
            absenceScore: 1f);
        Assert.True(withTower > withoutTower,
            $"tower aura should raise danger: with={withTower:F3} vs without={withoutTower:F3}");
        Assert.True(withTower >= 0.55f,
            $"Dire T1 bot with alive tower should be red/orange (≥0.55), got {withTower:F3}");
    }

    [Fact]
    public void V17_DeadEnemyTower_GivesNoAura_AllowsAbsenceCrush()
    {
        // Та сама точка — Dire T1 bot — але вежа знищена. Тоді tower aura ≈ 0
        // (інші вежі далеко), і absence-crush повертає зону до floor (~0.41).
        var towers = D2Helper.Core.Models.TowerSnapshot.AllAlive()
            .WithDestroyed(D2Helper.Core.Models.TowerTeam.Dire, D2Helper.Core.Models.TowerKey.T1Bot);
        var (tx, ty) = D2Helper.Core.Models.TowerMap.Dire[D2Helper.Core.Models.TowerKey.T1Bot];
        float aura = towers.SampleAura(tx, ty, playerIsRadiant: true);

        // Інші вежі далеко (Т2 bot Dire +1500, Т2 mid Dire +кілька тис) → aura < 0.4.
        Assert.True(aura < 0.4f, $"dead T1 bot should have weak residual aura, got {aura:F3}");

        var result = DangerZoneModel.ComputeDangerDynamic(
            wx: tx, wy: ty, side: PlayerSide.Radiant, gameTime: 1f,
            absenceScore: 1f, towerAuraLocal: aura);
        Assert.True(result < 0.55f, $"dead-tower zone should crush below red, got {result:F3}");
    }

    [Fact]
    public void V17_FriendlyTowerNearby_PullsDangerDown()
    {
        // Точка біля свого Т2 mid Radiant. Без aura — це межа лісу, danger ≈ 0.35-0.45.
        // З aura — присутність двох живих веж (Т2 + Т3 mid) тягне ще нижче.
        var towers = D2Helper.Core.Models.TowerSnapshot.AllAlive();
        var (tx, ty) = D2Helper.Core.Models.TowerMap.Radiant[D2Helper.Core.Models.TowerKey.T2Mid];
        float aura = towers.SampleAura(tx, ty, playerIsRadiant: true);
        Assert.True(aura < 0f, $"own tower zone should have negative aura (safety), got {aura:F3}");

        var withTower = DangerZoneModel.ComputeDangerDynamic(
            wx: tx, wy: ty, side: PlayerSide.Radiant, gameTime: 600f,
            towerAuraLocal: aura);
        var withoutTower = DangerZoneModel.ComputeDangerDynamic(
            wx: tx, wy: ty, side: PlayerSide.Radiant, gameTime: 600f);
        Assert.True(withTower < withoutTower,
            $"friendly tower aura should reduce danger: with={withTower:F3} vs without={withoutTower:F3}");
    }
}
