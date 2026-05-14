using System.Reactive.Linq;
using Dota2GSI;

namespace D2Helper.Core.Quests;

public sealed class QuestRunner : IQuestRunner
{
    private readonly ZoneCatalog _zones;

    public QuestRunner(ZoneCatalog zones)
    {
        _zones = zones;
    }

    public IObservable<QuestTick> Run(IObservable<GameState> states, PlaybookDefinition playbook)
    {
        var scheduler = new QuestScheduler();
        return states
            .Select(GameStateSnapshotExtractor.Extract)
            .Select(s => new QuestTick(s, scheduler.Tick(playbook, s)))
            .DistinctUntilChanged(TickComparer.Instance);
    }

    private sealed class TickComparer : IEqualityComparer<QuestTick>
    {
        public static readonly TickComparer Instance = new();

        public bool Equals(QuestTick? x, QuestTick? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            var a = x.Quests; var b = y.Quests;
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].QuestId != b[i].QuestId) return false;
                if (a[i].Current != b[i].Current) return false;
                if (a[i].Target != b[i].Target) return false;
                if (a[i].IsCompleted != b[i].IsCompleted) return false;
                if (a[i].Status != b[i].Status) return false;
            }
            return true;
        }

        public int GetHashCode(QuestTick obj)
        {
            var hash = new HashCode();
            foreach (var item in obj.Quests)
            {
                hash.Add(item.QuestId);
                hash.Add(item.Current);
                hash.Add(item.Target);
                hash.Add(item.IsCompleted);
                hash.Add(item.Status);
            }
            return hash.ToHashCode();
        }
    }
}

