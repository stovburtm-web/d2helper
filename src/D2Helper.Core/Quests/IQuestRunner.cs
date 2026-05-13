using Dota2GSI;

namespace D2Helper.Core.Quests;

public interface IQuestRunner
{
    IObservable<IReadOnlyList<QuestProgress>> Run(IObservable<GameState> states, PlaybookDefinition playbook);
}
