using Dota2GSI;

namespace D2Helper.Core.Quests;

public sealed record QuestTick(GameStateSnapshot Snapshot, IReadOnlyList<QuestProgress> Quests);

public interface IQuestRunner
{
    IObservable<QuestTick> Run(IObservable<GameState> states, PlaybookDefinition playbook);
}
