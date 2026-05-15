using System.Reactive.Linq;
using System.Reactive.Subjects;
using Dota2GSI;
using Dota2GSI.EventMessages;
using Dota2GSI.Nodes;

namespace D2Helper.Core.Gsi;

/// <summary>
/// Реактивний фасад над <see cref="GameStateListener"/>:
/// підіймає HTTP listener, ловить <c>NewGameState</c> від Dota 2 і ре-публікує його як <see cref="IObservable{GameState}"/>.
/// Дозволяє quest engine'у, overlay'ю та draft helper'у підписатись без поллінгу.
///
/// Окрім стрічки повних game-state'ів, виставляє типізовані стріми GSI-подій:
/// rune-pickup, bounty-pickup, kills, tower-kills тощо. Це **точніші тригери**,
/// ніж diff-based евристики у quest-engine: подія приходить рівно тоді, коли
/// дія відбулась, а не коли побічний показник (gold/xp/level) досяг порога.
/// </summary>
public sealed class GameStateBus : IDisposable
{
    private readonly GameStateListener _listener;
    private readonly BehaviorSubject<GameState?> _subject = new(null);
    private readonly BehaviorSubject<GsiStatus> _status = new(GsiStatus.Stopped);

    // Стрім сирих GSI gameplay-подій (events[] масив з cfg-у): bounty pickup, roshan_killed,
    // aegis_picked_up, courier_killed, tip. Емітимо кожну окрему подію після нового стейту.
    private readonly Subject<GameplayEvent> _gameplayEvents = new();
    // Зміни RunesActivated лічильника локального гравця — найточніший тригер для PickRune-квестів.
    private readonly Subject<int> _runesActivated = new();
    // Зміни LH/Denies/Kills/Deaths/Assists/Wards/Towers — для майбутньої score-системи.
    private readonly Subject<HeroDied> _heroDied = new();
    private readonly Subject<TowerDestroyed> _towerDestroyed = new();
    // V1.5: єдиний надійний сигнал смерті ворожого в normal play (не spectator):
    // Deaths-counter є public статистикою в GSI для всіх 10 гравців. Подія файриться рівно
    // в момент смерті (на відміну від HeroDied, яка в нормал 1v5 файриться лише для локального).
    private readonly Subject<PlayerDeathsChanged> _playerDeaths = new();

    public GameStateBus(int port = 3000)
    {
        Port = port;
        _listener = new GameStateListener(port);
        _listener.NewGameState += OnNewGameState;

        // Типізовані події приходять синтетично від Dota2EventsInterface (наш GameStateListener
        // успадковує від нього). Ми просто проксуємо їх у Rx-стріми.
        _listener.GameplayEvent += e => _gameplayEvents.OnNext(e);
        _listener.PlayerRunesActivatedChanged += e => _runesActivated.OnNext(e.New);
        _listener.HeroDied += e => _heroDied.OnNext(e);
        _listener.TowerDestroyed += e => _towerDestroyed.OnNext(e);
        _listener.PlayerDeathsChanged += e => _playerDeaths.OnNext(e);
    }

    public int Port { get; }

    /// <summary>Стрім усіх отриманих <see cref="GameState"/>. Останнє значення кешується (BehaviorSubject).</summary>
    public IObservable<GameState> States => _subject.Where(s => s is not null)!;

    /// <summary>Статус listener'у — для UI status-рядка.</summary>
    public IObservable<GsiStatus> Status => _status.DistinctUntilChanged();

    /// <summary>Окремі gameplay-події з масиву <c>events</c> (bounty pickup, roshan_killed, courier_killed, aegis…).</summary>
    public IObservable<GameplayEvent> GameplayEvents => _gameplayEvents;

    /// <summary>Нове значення лічильника <c>Player.RunesActivated</c> — миттєвий сигнал «руна підібрана».</summary>
    public IObservable<int> RunesActivated => _runesActivated;

    /// <summary>Подія смерті героя (своя + ворожі — для лічильника kills).</summary>
    public IObservable<HeroDied> HeroDied => _heroDied;

    /// <summary>Подія знищення вежі.</summary>
    public IObservable<TowerDestroyed> TowerDestroyed => _towerDestroyed;

    /// <summary>V1.5: зміна лічильника смертей будь-якого гравця. Для ворожих є єдиним надійним
    /// сигналом смерті в normal play. Подія містить <c>Player.PlayerID</c>, <c>Player.Team</c>, <c>New</c>, <c>Previous</c>.</summary>
    public IObservable<PlayerDeathsChanged> PlayerDeaths => _playerDeaths;

    /// <summary>
    /// <b>true</b> коли гравець у активному матчі (draft / pre-game / game in progress),
    /// <b>false</b> коли в головному меню, post-game stats screen, disconnect, або
    /// якщо GSI не прислав ні одного тіку >5с (Dota закрита / в меню).
    /// Використовується overlay-вікнами щоб ховатись поза грою.
    /// </summary>
    public IObservable<bool> InGame =>
        Observable.Merge(
            // Кожен GSI tick: чи стейт є «активним»?
            _subject.Select(gs => gs is not null && IsActiveState(gs.Map?.GameState)),
            // Watchdog: якщо за 5с нема жодного тіку — вважаємо що не в грі.
            _subject.Throttle(TimeSpan.FromSeconds(5)).Select(_ => false))
        .DistinctUntilChanged();

    private static bool IsActiveState(object? gameState)
    {
        if (gameState is null) return false;
        var s = gameState.ToString() ?? "";
        // Виключаємо неактивні стани. Усе інше (HERO_SELECTION, STRATEGY_TIME,
        // PRE_GAME, GAME_IN_PROGRESS, WAIT_FOR_*, TEAM_SHOWCASE, CUSTOM_GAME_SETUP)
        // вважаємо активним матчем.
        if (s.Contains("POST_GAME", StringComparison.Ordinal)) return false;
        if (s.Contains("DISCONNECT", StringComparison.Ordinal)) return false;
        if (s.Contains("INIT", StringComparison.Ordinal)) return false;
        if (s.Contains("Undefined", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Contains("LAST", StringComparison.Ordinal)) return false;
        return true;
    }

    public GameState? CurrentState => _subject.Value;

    /// <summary>
    /// Записує cfg-файл у папку Dota 2 (якщо знайдеться), щоб гра почала постити GSI.
    /// </summary>
    public bool EnsureConfigExists(string name = "d2helper")
    {
        try
        {
            return _listener.GenerateGSIConfigFile(name);
        }
        catch
        {
            return false;
        }
    }

    public bool Start()
    {
        var ok = _listener.Start();
        _status.OnNext(ok ? GsiStatus.WaitingForDota : GsiStatus.FailedToStart);
        return ok;
    }

    public void Stop()
    {
        _listener.Stop();
        _status.OnNext(GsiStatus.Stopped);
    }

    private void OnNewGameState(GameState gs)
    {
        _status.OnNext(GsiStatus.Connected);
        _subject.OnNext(gs);
    }

    public void Dispose()
    {
        _listener.NewGameState -= OnNewGameState;
        try { _listener.Stop(); } catch { /* ignore */ }
        _subject.OnCompleted();
        _status.OnCompleted();
        _gameplayEvents.OnCompleted();
        _runesActivated.OnCompleted();
        _heroDied.OnCompleted();
        _towerDestroyed.OnCompleted();
        _playerDeaths.OnCompleted();
    }
}

public enum GsiStatus
{
    Stopped,
    FailedToStart,
    WaitingForDota,
    Connected,
}
