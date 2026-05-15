using D2Helper.Core.Models;
using D2Helper.Vision;
using Xunit;

namespace D2Helper.Tests;

public class EnemyPresenceFieldTests
{
    [Fact]
    public void EmptySnapshot_PresenceZero_AbsenceZero()
    {
        var snap = new EnemyPresenceSnapshot(Array.Empty<EnemyDot>());
        Assert.Equal(0, snap.FreshCount);
        Assert.Equal(0f, snap.TotalMass);
        Assert.Equal(0f, snap.SampleLocal(0, 0));
        Assert.Equal(0f, snap.SampleAbsence(0, 0));
    }

    [Fact]
    public void SingleEnemy_Local_HighAtPos_LowFar()
    {
        var snap = new EnemyPresenceSnapshot(new[] { new EnemyDot(3000f, 3000f, 0f) });
        var atEnemy = snap.SampleLocal(3000f, 3000f);
        var far = snap.SampleLocal(-7000f, -7000f);
        Assert.True(atEnemy > 0.9f, $"at enemy expected ~1, got {atEnemy}");
        Assert.True(far < 0.05f, $"far expected ~0, got {far}");
    }

    [Fact]
    public void StaleDecay_HalvesAround6s()
    {
        var fresh = new EnemyPresenceSnapshot(new[] { new EnemyDot(0, 0, 0f) }).SampleLocal(0, 0);
        var aged = new EnemyPresenceSnapshot(new[] { new EnemyDot(0, 0, 6f) }).SampleLocal(0, 0);
        Assert.InRange(aged / fresh, 0.30f, 0.45f);
    }

    [Fact]
    public void StaleOver15s_DropsToZero()
    {
        Assert.Equal(0f, EnemyPresenceSnapshot.StaleWeight(15.1f));
        Assert.Equal(0f, EnemyPresenceSnapshot.StaleWeight(30f));
    }

    [Fact]
    public void Absence_Requires_AtLeast2FreshEnemies()
    {
        // 1 fresh enemy — впевненості мало → absence = 0 всюди.
        var snap = new EnemyPresenceSnapshot(new[] { new EnemyDot(3000, 3000, 0) });
        Assert.Equal(0f, snap.SampleAbsence(-7000, -7000));
    }

    [Fact]
    public void Absence_HighFar_LowNear_WhenManyEnemiesCluster()
    {
        // Кластер 4 ворогів у Dire-half. Зона у Radiant base має давати високу absence.
        var dots = new[]
        {
            new EnemyDot(5000, 5000, 0),
            new EnemyDot(5200, 4800, 0),
            new EnemyDot(4800, 5200, 0),
            new EnemyDot(5000, 4900, 0),
        };
        var snap = new EnemyPresenceSnapshot(dots);
        Assert.Equal(4, snap.FreshCount);

        var farFromCluster = snap.SampleAbsence(-6000, -6000);
        var atCluster = snap.SampleAbsence(5000, 5000);

        Assert.True(farFromCluster > 0.7f, $"far absence expected high, got {farFromCluster}");
        Assert.True(atCluster < 0.2f, $"at-cluster absence expected low, got {atCluster}");
    }

    [Fact]
    public void DangerDynamic_PresenceMakesSafeZoneDangerous()
    {
        // Radiant base — за geometric model danger низький.
        var baseDanger = DangerZoneModel.ComputeDangerDynamic(
            wx: -6500, wy: -6500, side: PlayerSide.Radiant, gameTime: 600);
        Assert.True(baseDanger < 0.3f, $"geometric Radiant base should be safe, got {baseDanger}");

        // Тепер ворог стоїть просто у твоїй базі — має стати небезпечно.
        var withEnemy = DangerZoneModel.ComputeDangerDynamic(
            wx: -6500, wy: -6500, side: PlayerSide.Radiant, gameTime: 600,
            presenceLocal: 1.0f);
        Assert.True(withEnemy > baseDanger + 0.4f,
            $"presence=1 should boost danger; was {baseDanger}, now {withEnemy}");
    }

    [Fact]
    public void DangerDynamic_AbsenceMakesDangerZoneSafer()
    {
        // Dire highground — за geometric model danger високий для Radiant.
        var baseDanger = DangerZoneModel.ComputeDangerDynamic(
            wx: 5500, wy: 5500, side: PlayerSide.Radiant, gameTime: 600);
        Assert.True(baseDanger > 0.7f, $"Dire highground should be dangerous, got {baseDanger}");

        // Якщо absence=1 (всі вороги десь в іншому місці) — має зменшитись помітно.
        var withAbsence = DangerZoneModel.ComputeDangerDynamic(
            wx: 5500, wy: 5500, side: PlayerSide.Radiant, gameTime: 600,
            absenceScore: 1.0f);
        Assert.True(withAbsence < baseDanger - 0.3f,
            $"absence=1 should drop danger; was {baseDanger}, now {withAbsence}");
    }

    [Fact]
    public void DangerDynamic_NoPresenceData_MatchesPureGeometric()
    {
        // NaN presence/absence не повинні міняти результат.
        float a = DangerZoneModel.ComputeDangerDynamic(2000, 2000, PlayerSide.Radiant, 600);
        float b = DangerZoneModel.ComputeDangerDynamic(2000, 2000, PlayerSide.Radiant, 600,
            presenceLocal: float.NaN, absenceScore: float.NaN);
        Assert.Equal(a, b);
    }
}
