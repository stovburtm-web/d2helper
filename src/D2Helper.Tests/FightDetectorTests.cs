using D2Helper.Core.Gsi;
using D2Helper.Core.Models;
using Xunit;

namespace D2Helper.Tests;

/// <summary>
/// V2.0 — детектор файтів. Перевіряємо:
///  - burst hp drop + ворог поряд = Teamfight,
///  - повільний rot/armlet без ворога = NIC (нічого),
///  - 2+ алі під атакою одночасно = Teamfight (cluster_allies),
///  - смерть алі = Teamfight (death),
///  - cooldown працює.
/// </summary>
public class FightDetectorTests
{
    private static EnemyPresenceSnapshot EnemiesAt(params (float x, float y)[] points)
    {
        var dots = new List<EnemyDot>();
        foreach (var p in points)
            dots.Add(new EnemyDot(p.x, p.y, StaleSeconds: 0f, Weight: 1f));
        return new EnemyPresenceSnapshot(dots);
    }

    private static EnemyPresenceSnapshot NoEnemies() => new(Array.Empty<EnemyDot>());

    [Fact]
    public void Burst_HpDrop_WithEnemyNearby_EmitsTeamfight()
    {
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((1000f, 1000f));

        // tick 0: алі здоровий
        var e0 = det.Update(new[] { new AllyTickInput(1, HpPct: 100, MpPct: 80, IsAlive: true, Wx: 1100, Wy: 1100) }, enemies, t0);
        Assert.Empty(e0);

        // через 1с — hp впав на 30% (burst >18%/1с → Teamfight)
        var t1 = t0.AddSeconds(1);
        var e1 = det.Update(new[] { new AllyTickInput(1, 70, 80, true, 1100, 1100) }, enemies, t1);
        Assert.Single(e1);
        Assert.Equal(FightSeverity.Teamfight, e1[0].Severity);
        Assert.Contains(e1[0].Reason, new[] { "burst1s", "burst2s" });
    }

    [Fact]
    public void SlowDrain_NoEnemyNearby_DoesNotTrigger()
    {
        // Симулюємо Pudge Rot ≈ 4%/с max-hp = за 2с впало ~8% (нижче порогу Skirmish 12%).
        // Плюс ворогів поряд нема. Має бути тиша.
        var det = new FightDetector();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int playerId = 2;

        for (int i = 0; i <= 20; i++) // 2с по 10Hz
        {
            float hp = 100f - i * 0.4f; // 0.4% за тік ≈ 4%/с
            var events = det.Update(
                new[] { new AllyTickInput(playerId, hp, 100, true, 0, 0) },
                NoEnemies(), t.AddMilliseconds(100 * i));
            Assert.Empty(events);
        }
    }

    [Fact]
    public void Burst_HpDrop_NoEnemyNearby_DoesNotTrigger()
    {
        // Навіть якщо hp впало різко — без видимого ворога вважаємо self-dmg/невідомо.
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        det.Update(new[] { new AllyTickInput(3, 100, 80, true, 0, 0) }, NoEnemies(), t0);
        var e = det.Update(new[] { new AllyTickInput(3, 60, 80, true, 0, 0) }, NoEnemies(), t0.AddSeconds(1));
        Assert.Empty(e);
    }

    [Fact]
    public void ModerateDrop_WithEnemyNearby_EmitsSkirmish()
    {
        // 14% за 2с ≥ 12% поріг Skirmish, але < 25%/2с і < 18%/1с → Skirmish.
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((500, 500));

        det.Update(new[] { new AllyTickInput(4, 100, 50, true, 600, 600) }, enemies, t0);
        // tick через 1с — впало 7% (ще не burst1s 18%)
        det.Update(new[] { new AllyTickInput(4, 93, 50, true, 600, 600) }, enemies, t0.AddSeconds(1));
        // tick через 2с — впало 14% від початку (Δhp2s = 14)
        var e = det.Update(new[] { new AllyTickInput(4, 86, 50, true, 600, 600) }, enemies, t0.AddSeconds(2));
        Assert.Single(e);
        Assert.Equal(FightSeverity.Skirmish, e[0].Severity);
    }

    [Fact]
    public void TwoAllies_UnderAttack_EscalateToTeamfight_ClusterAllies()
    {
        // Двоє алі одночасно отримали по 14% за 2с (Skirmish-рівень). Бо їх двох,
        // ескалюємо до Teamfight (cluster_allies).
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((500, 500), (-500, -500));

        det.Update(new[]
        {
            new AllyTickInput(5, 100, 50, true, 600, 600),
            new AllyTickInput(6, 100, 50, true, -600, -600),
        }, enemies, t0);

        var e = det.Update(new[]
        {
            new AllyTickInput(5, 86, 50, true, 600, 600),
            new AllyTickInput(6, 86, 50, true, -600, -600),
        }, enemies, t0.AddSeconds(2));

        Assert.Single(e); // одна кластерна подія
        Assert.Equal(FightSeverity.Teamfight, e[0].Severity);
        Assert.Equal("cluster_allies", e[0].Reason);
        Assert.Equal(2, e[0].InvolvedAllyIds.Count);
    }

    [Fact]
    public void Death_EmitsTeamfight_AtLastLivePosition()
    {
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = NoEnemies();

        det.Update(new[] { new AllyTickInput(7, 100, 50, true, 1234, 5678) }, enemies, t0);
        det.Update(new[] { new AllyTickInput(7, 50, 30, true, 1234, 5678) }, enemies, t0.AddSeconds(0.5));
        var dead = det.Update(new[] { new AllyTickInput(7, 0, 0, IsAlive: false, 0, 0) }, enemies, t0.AddSeconds(1));

        Assert.Single(dead);
        Assert.Equal(FightSeverity.Teamfight, dead[0].Severity);
        Assert.Equal("death", dead[0].Reason);
        Assert.Equal(1234, dead[0].Wx);
        Assert.Equal(5678, dead[0].Wy);
    }

    [Fact]
    public void Cooldown_SuppressesRepeatEventsOnSameAlly()
    {
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((500, 500));

        det.Update(new[] { new AllyTickInput(8, 100, 50, true, 600, 600) }, enemies, t0);
        var e1 = det.Update(new[] { new AllyTickInput(8, 70, 50, true, 600, 600) }, enemies, t0.AddSeconds(1));
        Assert.Single(e1);

        // ще через 0.5с — той самий алі продовжує "втрачати hp" — cooldown має задавити дубль.
        det.Update(new[] { new AllyTickInput(8, 65, 50, true, 600, 600) }, enemies, t0.AddSeconds(1.5));
        var e2 = det.Update(new[] { new AllyTickInput(8, 50, 50, true, 600, 600) }, enemies, t0.AddSeconds(2));
        Assert.Empty(e2);

        // після 6с (Teamfight cooldown) — знову можемо тригерити.
        det.Update(new[] { new AllyTickInput(8, 100, 50, true, 600, 600) }, enemies, t0.AddSeconds(8));
        var e3 = det.Update(new[] { new AllyTickInput(8, 70, 50, true, 600, 600) }, enemies, t0.AddSeconds(9));
        Assert.Single(e3);
    }

    [Fact]
    public void ThreeEnemiesClustered_EscalatesSingleAllyToTeamfight()
    {
        // Один алі отримав 14% за 2с (Skirmish), але поряд кластер 3+ ворогів → Teamfight.
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((500, 500), (700, 400), (300, 600));

        det.Update(new[] { new AllyTickInput(9, 100, 50, true, 600, 500) }, enemies, t0);
        var e = det.Update(new[] { new AllyTickInput(9, 86, 50, true, 600, 500) }, enemies, t0.AddSeconds(2));
        Assert.Single(e);
        Assert.Equal(FightSeverity.Teamfight, e[0].Severity);
        Assert.Equal("cluster_enemies", e[0].Reason);
    }

    // ===================== V2.1: CC edge =====================

    [Fact]
    public void CcEdge_Stunned_WithEnemyNearby_EmitsTeamfight()
    {
        // None → Stunned + ворог в межах 2500 → миттєвий Teamfight cc_stunned
        // (стан HardDisable = Stunned|Hexed автоматично ескалює до Teamfight).
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((1500, 1500)); // в межах 2500 від алі

        det.Update(new[] { new AllyTickInput(10, 100, 80, true, 100, 100, Cc: CcFlags.None) },
                   enemies, t0);
        var e = det.Update(new[] { new AllyTickInput(10, 100, 80, true, 100, 100, Cc: CcFlags.Stunned) },
                   enemies, t0.AddMilliseconds(100));
        Assert.Single(e);
        Assert.Equal(FightSeverity.Teamfight, e[0].Severity);
        Assert.Equal("cc_stunned", e[0].Reason);
    }

    [Fact]
    public void CcEdge_Silenced_OnSupport_EmitsSkirmish()
    {
        // Silenced — не hard CC, тому Skirmish (не Teamfight).
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((1500, 1500));

        det.Update(new[] { new AllyTickInput(11, 100, 80, true, 100, 100, Cc: CcFlags.None) }, enemies, t0);
        var e = det.Update(new[] { new AllyTickInput(11, 100, 80, true, 100, 100, Cc: CcFlags.Silenced) },
                   enemies, t0.AddMilliseconds(100));
        Assert.Single(e);
        Assert.Equal(FightSeverity.Skirmish, e[0].Severity);
        Assert.Equal("cc_silenced", e[0].Reason);
    }

    [Fact]
    public void CcEdge_NoEnemyAnywhere_DoesNotTrigger()
    {
        // Stun від lasthit-dust / brown buff від нейтралки без жодного ворога в 2500 — тиша.
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        det.Update(new[] { new AllyTickInput(12, 100, 80, true, 0, 0, Cc: CcFlags.None) }, NoEnemies(), t0);
        var e = det.Update(new[] { new AllyTickInput(12, 100, 80, true, 0, 0, Cc: CcFlags.Stunned) },
                   NoEnemies(), t0.AddMilliseconds(100));
        Assert.Empty(e);
    }

    [Fact]
    public void CcEdge_TwoAlliesStunnedSameTick_EmitsClusterCc()
    {
        // Класичний Naga sleep / Magnus RP / ES Echo — кілька алі CC за один тік.
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((0, 0));

        det.Update(new[]
        {
            new AllyTickInput(13, 100, 80, true,  500, 500, Cc: CcFlags.None),
            new AllyTickInput(14, 100, 80, true, -500, -500, Cc: CcFlags.None),
        }, enemies, t0);
        var e = det.Update(new[]
        {
            new AllyTickInput(13, 100, 80, true,  500,  500, Cc: CcFlags.Stunned),
            new AllyTickInput(14, 100, 80, true, -500, -500, Cc: CcFlags.Stunned),
        }, enemies, t0.AddMilliseconds(100));

        Assert.Single(e);
        Assert.Equal(FightSeverity.Teamfight, e[0].Severity);
        Assert.Equal("cluster_cc", e[0].Reason);
        Assert.Equal(2, e[0].InvolvedAllyIds.Count);
    }

    [Fact]
    public void CcEdge_StaysStunned_DoesNotReTrigger()
    {
        // Поки прапор тримається — повторно не файрить.
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((1500, 1500));

        det.Update(new[] { new AllyTickInput(15, 100, 80, true, 100, 100, Cc: CcFlags.None) },    enemies, t0);
        var e1 = det.Update(new[] { new AllyTickInput(15, 100, 80, true, 100, 100, Cc: CcFlags.Stunned) },
                   enemies, t0.AddMilliseconds(100));
        Assert.Single(e1);
        var e2 = det.Update(new[] { new AllyTickInput(15, 100, 80, true, 100, 100, Cc: CcFlags.Stunned) },
                   enemies, t0.AddMilliseconds(500));
        Assert.Empty(e2);
        var e3 = det.Update(new[] { new AllyTickInput(15, 100, 80, true, 100, 100, Cc: CcFlags.Stunned) },
                   enemies, t0.AddSeconds(1));
        Assert.Empty(e3);
    }

    // ===================== V2.2: prefight signals =====================

    [Fact]
    public void PrefightCluster_NoBurst_EmitsSkirmish()
    {
        // 3+ ворогів у 1500 від алі, але HP не падало → prefight_cluster (Skirmish).
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((500, 500), (700, 400), (300, 600));

        var e = det.Update(new[] { new AllyTickInput(16, 100, 50, true, 600, 500) }, enemies, t0);
        Assert.Single(e);
        Assert.Equal(FightSeverity.Skirmish, e[0].Severity);
        Assert.Equal("prefight_cluster", e[0].Reason);
    }

    [Fact]
    public void Isolated_SoloSupportFarFromTeam_EmitsSkirmish()
    {
        // Алі #17 знаходиться біля ворога; найближчий інший живий алі — 5000 unit'ів далі.
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((600, 600));

        var allies = new[]
        {
            new AllyTickInput(17, 100, 50, true, 500, 500),
            new AllyTickInput(18, 100, 50, true, 5500, 5500), // дуже далеко
        };
        var e = det.Update(allies, enemies, t0);
        // Алі 17 поряд з ворогом, алі 18 — далеко від ворога (ні burst, ні CC). Має бути 1 isolated на 17.
        Assert.Contains(e, ev => ev.Reason == "isolated" && ev.InvolvedAllyIds[0] == 17);
        Assert.DoesNotContain(e, ev => ev.InvolvedAllyIds[0] == 18 && ev.Reason == "isolated");
    }

    [Fact]
    public void Isolated_AllyNearby_DoesNotTrigger()
    {
        // Один ворог поряд, інший алі за 800 unit'ів — це НЕ ізольований випадок.
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((600, 600));

        var allies = new[]
        {
            new AllyTickInput(19, 100, 50, true, 500, 500),
            new AllyTickInput(20, 100, 50, true, 1300, 500), // 800 unit'ів від 19
        };
        var e = det.Update(allies, enemies, t0);
        Assert.DoesNotContain(e, ev => ev.Reason == "isolated");
    }

    [Fact]
    public void PrefightCluster_SuppressedDuringBurstCooldown()
    {
        // Якщо у цей же тік burst → prefight для того ж алі НЕ файрить (вже й так Teamfight).
        var det = new FightDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var enemies = EnemiesAt((500, 500), (700, 400), (300, 600));

        det.Update(new[] { new AllyTickInput(21, 100, 50, true, 600, 500) }, enemies, t0);
        var e = det.Update(new[] { new AllyTickInput(21, 60, 50, true, 600, 500) }, enemies, t0.AddSeconds(1));
        // Має бути одна burst-подія (cluster_enemies → Teamfight); prefight не дублюється.
        Assert.Single(e);
        Assert.Equal(FightSeverity.Teamfight, e[0].Severity);
        Assert.DoesNotContain(e, ev => ev.Reason == "prefight_cluster" || ev.Reason == "isolated");
    }
}
