using System.Reactive.Linq;
using System.Reactive.Subjects;
using Dota2GSI;
using Dota2GSI.Nodes;

namespace D2Helper.Core.Gsi;

/// <summary>
/// Реактивний фасад над <see cref="GameStateListener"/>:
/// підіймає HTTP listener, ловить <c>NewGameState</c> від Dota 2 і ре-публікує його як <see cref="IObservable{GameState}"/>.
/// Дозволяє quest engine'у, overlay'ю та draft helper'у підписатись без поллінгу.
/// </summary>
public sealed class GameStateBus : IDisposable
{
    private readonly GameStateListener _listener;
    private readonly BehaviorSubject<GameState?> _subject = new(null);
    private readonly BehaviorSubject<GsiStatus> _status = new(GsiStatus.Stopped);

    public GameStateBus(int port = 3000)
    {
        Port = port;
        _listener = new GameStateListener(port);
        _listener.NewGameState += OnNewGameState;
    }

    public int Port { get; }

    /// <summary>Стрім усіх отриманих <see cref="GameState"/>. Останнє значення кешується (BehaviorSubject).</summary>
    public IObservable<GameState> States => _subject.Where(s => s is not null)!;

    /// <summary>Статус listener'у — для UI status-рядка.</summary>
    public IObservable<GsiStatus> Status => _status.DistinctUntilChanged();

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
    }
}

public enum GsiStatus
{
    Stopped,
    FailedToStart,
    WaitingForDota,
    Connected,
}
