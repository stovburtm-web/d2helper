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
    public void DangerDynamic_AbsenceCrushesEverywhere_PresenceOverrides()
    {
        // V1.6: absence-crush діє ВСЮДИ без gate. Що тримає ворожу зону червоною —
        // це наявність presenceLocal там (вороги фізично там і є).

        // Випадок А: своя territory + absence=1 (вороги деінде) → знижується.
        var ownBase = DangerZoneModel.ComputeDangerDynamic(
            wx: -3000, wy: -3000, side: PlayerSide.Radiant, gameTime: 600);
        var ownWithAbsence = DangerZoneModel.ComputeDangerDynamic(
            wx: -3000, wy: -3000, side: PlayerSide.Radiant, gameTime: 600,
            absenceScore: 1.0f);
        Assert.True(ownWithAbsence < ownBase - 0.05f,
            $"own zone with absence=1 should drop; was {ownBase}, now {ownWithAbsence}");

        // Випадок Б: Dire highground БЕЗ presence-інфо + absence=1 — знижується АЛЕ
        // не до зеленого (V1.6.1 floor: tower aggression тримає мінімум). На gameTime=600
        // floor ≈ 0.70 * base. Має бути нижче за base, але вище ~0.55.
        var enemyBase = DangerZoneModel.ComputeDangerDynamic(
            wx: 5500, wy: 5500, side: PlayerSide.Radiant, gameTime: 600);
        var enemyWithAbsenceOnly = DangerZoneModel.ComputeDangerDynamic(
            wx: 5500, wy: 5500, side: PlayerSide.Radiant, gameTime: 600,
            absenceScore: 1.0f);
        Assert.True(enemyWithAbsenceOnly < enemyBase,
            $"enemy HG with absence drops below base ({enemyBase:F3}); got {enemyWithAbsenceOnly:F3}");
        Assert.True(enemyWithAbsenceOnly > 0.55f,
            $"enemy HG at mid-game with floor — should not be safe; got {enemyWithAbsenceOnly:F3}");

        // Випадок В: Dire highground З presenceLocal (вороги саме там) → лишається червоним
        // навіть якщо absenceScore (метрика "всі купкою деінде") теж високий.
        var enemyWithPresence = DangerZoneModel.ComputeDangerDynamic(
            wx: 5500, wy: 5500, side: PlayerSide.Radiant, gameTime: 600,
            presenceLocal: 1.0f, absenceScore: 0.5f);
        Assert.True(enemyWithPresence > 0.7f,
            $"enemy highground with presence locally stays red; got {enemyWithPresence}");
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

    // ===== V1.4: friendly control / lane creep weight =====

    [Fact]
    public void EnemyDot_CustomWeight_ScalesPresence()
    {
        var heroSnap = new EnemyPresenceSnapshot(new[] { new EnemyDot(0, 0, 0f, Weight: 1f) });
        var creepSnap = new EnemyPresenceSnapshot(new[] { new EnemyDot(0, 0, 0f, Weight: 0.15f) });
        var heroLocal = heroSnap.SampleLocal(0, 0);
        var creepLocal = creepSnap.SampleLocal(0, 0);
        Assert.InRange(creepLocal / heroLocal, 0.10f, 0.20f);
    }

    [Fact]
    public void DangerDynamic_FriendlyControl_LowersBaseDanger()
    {
        // Ворожий highground без жодного контексту.
        float baseD = DangerZoneModel.ComputeDangerDynamic(5500, 5500, PlayerSide.Radiant, 600);
        // Купа союзників (fc=1.0) поряд — приймемо файт.
        float withAllies = DangerZoneModel.ComputeDangerDynamic(5500, 5500, PlayerSide.Radiant, 600,
            friendlyControl: 1.0f);
        Assert.True(withAllies < baseD - 0.3f,
            $"friendly control should drop danger; was {baseD}, now {withAllies}");
    }

    [Fact]
    public void DangerDynamic_EnemyVsFriendly_BothApplied()
    {
        // Ворог поряд (+0.6 nudge) + союзники поряд (-0.4) → майже нейтрально.
        float withBoth = DangerZoneModel.ComputeDangerDynamic(0, 0, PlayerSide.Radiant, 600,
            presenceLocal: 1.0f, friendlyControl: 1.0f);
        float onlyEnemy = DangerZoneModel.ComputeDangerDynamic(0, 0, PlayerSide.Radiant, 600,
            presenceLocal: 1.0f);
        Assert.True(withBoth < onlyEnemy - 0.3f,
            $"allies should reduce danger when contested; only={onlyEnemy} both={withBoth}");
    }

    [Fact]
    public void DangerDynamic_FriendlyControl_NaN_NoEffect()
    {
        float a = DangerZoneModel.ComputeDangerDynamic(2000, 2000, PlayerSide.Radiant, 600);
        float b = DangerZoneModel.ComputeDangerDynamic(2000, 2000, PlayerSide.Radiant, 600,
            friendlyControl: float.NaN);
        Assert.Equal(a, b);
    }

    // ===== V1.6: passive-dot pushes presence, absence has no gate =====

    [Fact]
    public void V16_PassiveDot_StillPushesPresence_KeepingFountainHot()
    {
        // Ворог у фонтані (passive=true) має пушити presence НА СВОЇЙ ПОЗИЦІЇ.
        // Це тримає ворожий фонтан "червоним" і робить absence там нульовим.
        var dots = new[]
        {
            new EnemyDot(7000f, 7000f, 0f, Weight: 1f, IsPassive: true),
            new EnemyDot(5000f, 5000f, 0f, Weight: 1f, IsPassive: false),
        };
        var snap = new EnemyPresenceSnapshot(dots);

        // Біля passive-крапки local має бути не менший за біля активної (обидва — повна вага).
        var atPassive = snap.SampleLocal(7000f, 7000f);
        var atActive = snap.SampleLocal(5000f, 5000f);
        Assert.True(atPassive > 0.5f,
            $"passive повинна пушити presence у своїй позиції; got {atPassive}");
        Assert.True(Math.Abs(atPassive - atActive) < 0.3f,
            $"passive і active мають давати схожий локальний push; passive={atPassive}, active={atActive}");

        // На самій passive-точці absence ≈ 0 (бо ratio ≈ 0).
        Assert.True(snap.SampleAbsence(7000f, 7000f) < 0.3f,
            "absence біля passive-крапки має бути низьким");
    }

    [Fact]
    public void V15_Absence_Confidence_ScalesWithAliveCount()
    {
        // 2 fresh enemies + AliveEnemyCount=3 (бо 2 ворога мертві) →
        // denominator = max(1, 3-1) = 2 → confidence = min(1, 2/2) = 1.0 (повна).
        // Без V1.5 denominator=4 → confidence = 0.5 (втричі менша зелень).
        var dots = new[]
        {
            new EnemyDot(5000, 5000, 0),
            new EnemyDot(5200, 4800, 0),
        };
        var snapAlive5 = new EnemyPresenceSnapshot(dots) { AliveEnemyCount = 5 };
        var snapAlive3 = new EnemyPresenceSnapshot(dots) { AliveEnemyCount = 3 };

        float far5 = snapAlive5.SampleAbsence(-6000, -6000);
        float far3 = snapAlive3.SampleAbsence(-6000, -6000);
        Assert.True(far3 > far5 + 0.2f,
            $"при меншій aliveCount absence сильніша; alive5={far5}, alive3={far3}");
    }
}
