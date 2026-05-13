using System.Collections.ObjectModel;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2Helper.Core;
using D2Helper.Core.Gsi;
using D2Helper.Core.Models;
using D2Helper.Core.Quests;
using D2Helper.Data.OpenDota;
using D2Helper.Data.Steam;
using D2Helper.Data.Stratz;
using Dota2GSI;

namespace D2Helper.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly OpenDotaClient _openDota;
    private readonly StratzClient _stratz;
    private readonly SteamOpenIdService _steamAuth;
    private readonly GameStateBus _gsi;

    public MainWindowViewModel()
    {
        var http1 = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var http2 = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var http3 = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _openDota = new OpenDotaClient(http1);
        // Stratz API token — спершу з secrets/tokens.env, потім fallback на env var.
        _stratz = new StratzClient(http2, TokenStore.Get("STRATZ_TOKEN"));
        _steamAuth = new SteamOpenIdService(http3);

        // GSI bus — слухаємо на :3000.
        _gsi = new GameStateBus(3000);
        _gsi.Status
            .Subscribe(s => Dispatcher.UIThread.Post(() => GsiStatus = s switch
            {
                Core.Gsi.GsiStatus.Stopped       => "GSI: stopped",
                Core.Gsi.GsiStatus.FailedToStart => "GSI: failed to start (порт зайнятий?)",
                Core.Gsi.GsiStatus.WaitingForDota => "GSI: waiting for Dota 2…",
                Core.Gsi.GsiStatus.Connected     => "GSI: connected",
                _ => "GSI: ?",
            }));
        _gsi.States
            .Sample(TimeSpan.FromMilliseconds(250))
            .Subscribe(gs => Dispatcher.UIThread.Post(() => UpdateGsiSnapshot(gs)));
        _gsi.Start();

        try
        {
            var playbook = PlaybookLoader.LoadMidMvp();
            var zones = ZoneCatalog.LoadDefault();
            // Початковий рендер: показуємо квести з нульовим прогресом ще до першого GSI-стейту.
            var initialScheduler = new QuestScheduler();
            var initial = initialScheduler.Tick(playbook, new GameStateSnapshot());
            UpdateQuestProgress(QuestScheduler.SelectVisible(initial));
            IQuestRunner runner = new QuestRunner(zones);
            runner
                .Run(_gsi.States.Sample(TimeSpan.FromMilliseconds(250)), playbook)
                .Subscribe(q => Dispatcher.UIThread.Post(() => UpdateQuestProgress(QuestScheduler.SelectVisible(q))));
        }
        catch (Exception ex)
        {
            StatusMessage = "Quest engine init failed: " + ex.Message;
        }

        SteamIdInput = "76561198360734673";
    }

    [ObservableProperty] private string _gsiStatus = "GSI: starting…";
    [ObservableProperty] private string _gsiClock = "";
    [ObservableProperty] private string _gsiMatch = "";
    [ObservableProperty] private string _gsiHero = "";

    private void UpdateGsiSnapshot(GameState gs)
    {
        var clock = gs.Map.ClockTime;
        var sign = clock < 0 ? "-" : "";
        var abs = Math.Abs(clock);
        GsiClock = $"clock {sign}{abs / 60:D2}:{abs % 60:D2}";
        GsiMatch = gs.Map.MatchID > 0 ? $"match {gs.Map.MatchID}" : $"state {gs.Map.GameState}";
        var h = gs.Hero.LocalPlayer;
        GsiHero = string.IsNullOrEmpty(h.Name) ? "" : $"{h.Name} lvl{h.Level} hp{h.HealthPercent}%";
    }

    [RelayCommand]
    private void SetupGsi()
    {
        var ok = _gsi.EnsureConfigExists("d2helper");
        StatusMessage = ok
            ? "GSI cfg записано. Запусти/перезапусти Dota 2. GSI POST-и йдуть ЛИШЕ під час матчу (не в головному меню)."
            : "Не вдалось знайти папку Dota 2 — встанови гру або зроби cfg вручну.";
    }

    [RelayCommand]
    private void RemoveGsi()
    {
        var paths = GsiSetup.FindLikelyCfgPaths("d2helper");
        var removed = 0;
        foreach (var p in paths)
        {
            try { if (File.Exists(p)) { File.Delete(p); removed++; } } catch { /* ignore */ }
        }
        StatusMessage = removed > 0
            ? $"Видалено {removed} d2helper cfg. Якщо Dota не запускалась — спробуй знову."
            : "d2helper cfg не знайдено (немає що видаляти).";
    }

    [ObservableProperty] private string _steamIdInput = "76561198360734673";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _openDotaName = "—";
    [ObservableProperty] private string _openDotaRank = "";
    [ObservableProperty] private string _openDotaWinRate = "";
    [ObservableProperty] private string _openDotaError = "";

    [ObservableProperty] private string _stratzName = "—";
    [ObservableProperty] private string _stratzRank = "";
    [ObservableProperty] private string _stratzWinRate = "";
    [ObservableProperty] private string _stratzBehavior = "";
    [ObservableProperty] private string _stratzImp = "";
    [ObservableProperty] private string _stratzLastMatch = "";
    [ObservableProperty] private string _stratzError = "";

    public ObservableCollection<RecentMatch> RecentMatches { get; } = new();
    public ObservableCollection<QuestProgress> ActiveQuests { get; } = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!SteamIdUtil.TryParseSteamId64(SteamIdInput, out var steamId64))
        {
            StatusMessage = "Неправильний Steam ID";
            return;
        }

        IsLoading = true;
        StatusMessage = "Завантаження…";
        OpenDotaError = StratzError = "";
        RecentMatches.Clear();

        var odTask = LoadOpenDotaAsync(steamId64);
        var stTask = LoadStratzAsync(steamId64);
        var rmTask = LoadRecentAsync(steamId64);
        await Task.WhenAll(odTask, stTask, rmTask);

        IsLoading = false;
        StatusMessage = "Готово";
    }

    [RelayCommand]
    private async Task SignInWithSteamAsync()
    {
        IsLoading = true;
        StatusMessage = "Відкрив браузер — увійди через Steam…";
        try
        {
            var steamId = await _steamAuth.SignInAsync();
            if (steamId is null)
            {
                StatusMessage = "Sign-in скасовано або не вдалось";
                return;
            }
            SteamIdInput = steamId.Value.ToString();
            StatusMessage = "Steam OK. Тягну дані…";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "Помилка: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadOpenDotaAsync(long steamId64)
    {
        try
        {
            var p = await _openDota.GetPlayerAsync(steamId64);
            if (p is null) { OpenDotaError = "Гравця не знайдено / профіль приватний"; return; }
            OpenDotaName = string.IsNullOrEmpty(p.PersonaName) ? "(приватний нік)" : p.PersonaName;
            OpenDotaRank = $"Rank: {p.RankName}";
            OpenDotaWinRate = $"W/L: {p.Wins}/{p.Losses}  ({p.WinRate:P1})";
        }
        catch (Exception ex) { OpenDotaError = ex.Message; }
    }

    private async Task LoadStratzAsync(long steamId64)
    {
        try
        {
            var s = await _stratz.GetPlayerSummaryAsync(steamId64);
            if (s is null)
            {
                StratzError = "Stratz: " + (_stratz.LastError ?? "даних нема (можливо, не вистачає STRATZ_TOKEN у secrets/tokens.env)");
                return;
            }
            StratzName = string.IsNullOrEmpty(s.Name) ? "(anonymous)" : s.Name;
            StratzRank = $"Rank: {RankFormatter.Render(s.SeasonRank, s.LeaderboardRank)}";
            StratzWinRate = $"W/L: {s.WinCount}/{s.MatchCount - s.WinCount}  ({s.WinRate:P1})";
            StratzBehavior = s.BehaviorScore is null ? "Behavior: n/a" : $"Behavior: {s.BehaviorScore}";
            StratzImp = s.Imp is null ? "" : $"IMP (avg impact): {s.Imp}";
            StratzLastMatch = s.LastMatch is null ? "" : $"Last match: {s.LastMatch:yyyy-MM-dd HH:mm}";
        }
        catch (Exception ex) { StratzError = ex.Message; }
    }

    private async Task LoadRecentAsync(long steamId64)
    {
        try
        {
            var matches = await _openDota.GetRecentMatchesAsync(steamId64, 20);
            foreach (var m in matches) RecentMatches.Add(m);
        }
        catch (Exception ex) { OpenDotaError += " | Recent: " + ex.Message; }
    }

    private void UpdateQuestProgress(IReadOnlyList<QuestProgress> quests)
    {
        ActiveQuests.Clear();
        foreach (var q in quests) ActiveQuests.Add(q);
    }
}
