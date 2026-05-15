using D2Helper.Core.Gsi;
using Xunit;

namespace D2Helper.Tests;

public class MinimapPresenceTrackerTests
{
    // ===== V1.5: MarkEnemyDied зменшує AliveEnemyCount у наступному snapshot =====

    [Fact]
    public void MarkEnemyDied_DecrementsAliveEnemyCount_UntilRespawn()
    {
        var tracker = new MinimapPresenceTracker();
        var now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Початково ніхто не мертвий → 5 живих.
        var snap0 = tracker.Update(null, myIsRadiant: true, now);
        Assert.Equal(5, snap0.AliveEnemyCount);

        // 2 ворога померли з респавном 30с.
        tracker.MarkEnemyDied(playerId: 5, now: now, respawnSec: 30f);
        tracker.MarkEnemyDied(playerId: 6, now: now, respawnSec: 30f);
        var snap1 = tracker.Update(null, myIsRadiant: true, now);
        Assert.Equal(3, snap1.AliveEnemyCount);

        // Через 35с обидва вже мали воскреснути → AliveEnemyCount знову 5.
        var later = now.AddSeconds(35);
        var snap2 = tracker.Update(null, myIsRadiant: true, later);
        Assert.Equal(5, snap2.AliveEnemyCount);
    }

    [Fact]
    public void Reset_ClearsDeathTimers()
    {
        var tracker = new MinimapPresenceTracker();
        var now = DateTime.UtcNow;
        tracker.MarkEnemyDied(5, now, respawnSec: 60f);
        tracker.Reset();
        var snap = tracker.Update(null, myIsRadiant: true, now);
        Assert.Equal(5, snap.AliveEnemyCount);
    }
}
