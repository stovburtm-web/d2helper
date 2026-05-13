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

    public IObservable<IReadOnlyList<QuestProgress>> Run(IObservable<GameState> states, PlaybookDefinition playbook)
    {
        var scheduler = new QuestScheduler();
        return states
            .Select(GameStateSnapshotExtractor.Extract)
            .Select(s => scheduler.Tick(playbook, s))
            .DistinctUntilChanged(ProgressComparer.Instance);
    }

    private sealed class ProgressComparer : IEqualityComparer<IReadOnlyList<QuestProgress>>
    {
        public static readonly ProgressComparer Instance = new();

        public bool Equals(IReadOnlyList<QuestProgress>? x, IReadOnlyList<QuestProgress>? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null || x.Count != y.Count) return false;
            for (var i = 0; i < x.Count; i++)
            {
                if (x[i].QuestId != y[i].QuestId) return false;
                if (x[i].Current != y[i].Current) return false;
                if (x[i].Target != y[i].Target) return false;
                if (x[i].IsCompleted != y[i].IsCompleted) return false;
                if (x[i].Status != y[i].Status) return false;
            }
            return true;
        }

        public int GetHashCode(IReadOnlyList<QuestProgress> obj)
        {
            var hash = new HashCode();
            foreach (var item in obj)
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

