# D2Helper — Roadmap (MVP)

> Жива дорожня карта. Кожен етап має чіткий **deliverable** і **definition of done**. Етапи послідовні: не починаємо наступний поки попередній не зачинений.

## Концепція в одному реченні
Десктоп-комп'ютер для гри в Dota 2 на двох моніторах: на ігровому — прозорий **overlay з квестами + айтем-рекомендаціями + таймінгами**, на другому — **companion-вікно з draft-помічником і повним матчевим dashboard'ом**.

## Дві поверхні

### A. In-game overlay (ігровий монітор)
- **A1. Ліва панель.** Enemy intel (5 ворогів — MMR, top-3 героїв, last-20 W/L) + динамічний блок Recommended items vs lineup.
- **A2. Центр.** Quest HUD — 1-3 активних квести, прогрес-бар, таймер до наступного stage'а. Геймифікація для конкретної ролі.
- **A3. Права сторона.** Timing alerts — Roshan, Tormentor, rune, day/night, smoke gank warning.

### B. Companion window (другий монітор)
- **B1.** Draft helper з 3 фазами піків.
- **B2.** Live match dashboard — повна таблиця 10 гравців, net worth diff, history, GSI combat-log.
- **B3.** Post-match review (через OpenDota replay parse).

## Етапи

### Stage 1 — GSI integration ⏳ *поточний*
**Deliverable:** echo-server який приймає GSI POST'и від Dota 2 і логує `NewGameState` у консоль.

**DoD:**
- [ ] Додано NuGet `Dota2GSI` у `D2Helper.Core`
- [ ] Згенеровано `gamestate_integration_d2helper.cfg` у Dota 2 cfg-папку (через метод `GenerateGSIConfigFile`)
- [ ] HTTP listener на `:3000` приймає payload
- [ ] `GameStateBus` як `IObservable<GameState>` (Rx.NET)
- [ ] У companion-вікні рядок "GSI: connected / map=… / clock=…"
- [ ] ADR `docs/adr/0001-gsi-integration.md`

### Stage 2 — Quest engine prototype
**Deliverable:** один playbook (фейковий, 2-3 квести), engine рахує прогрес, у companion-вікні таблиця квестів з прогрес-барами.

**DoD:**
- [ ] `IQuestRunner` приймає `IObservable<GameState>`, видає `IObservable<QuestProgress>`
- [ ] Quest types: `GoldSpentQuest`, `DeniesQuest`, `WardsPlacedQuest`, `PositionInZoneQuest`
- [ ] JSON-формат `data/playbooks/role5.sample.json` (1 stage, 3 квести)
- [ ] Zone-matcher з хардкод-координатами (offlane camp, midlane T1, etc. у `data/zones.json`)
- [ ] Юніт-тести на progress calculation (xUnit)
- [ ] ADR `docs/adr/0002-quest-engine.md`

### Stage 3 — Перший прозорий overlay
**Deliverable:** прозоре topmost-вікно з Roshan-таймером, з'являється коли GSI шле `events.roshan_killed`.

**DoD:**
- [ ] Новий проєкт `src/D2Helper.Overlay/` (Avalonia, `TransparencyLevelHint=Transparent`, `Topmost=True`, click-through через `WS_EX_TRANSPARENT`)
- [ ] Reactive subscription на `GameStateBus` → коли Roshan вмер, показати countdown 8/11 хв
- [ ] Hotkey (default `F8`) щоб показати/сховати overlay
- [ ] Multi-monitor: overlay прив'язується до того монітора де відкрите вікно Dota
- [ ] ADR `docs/adr/0003-overlay-window.md` з обґрунтуванням Avalonia vs GameOverlay.Net

### Stage 4 — Draft helper (companion B1)
**Deliverable:** окремий tab у companion-вікні, відкривається коли GSI каже `map.game_state = DOTA_GAMERULES_STATE_HERO_SELECTION`. Показує кандидатів-героїв для твоєї ролі.

**DoD:**
- [ ] Stratz GraphQL клієнт: метод `GetHeroWinRatesAsync(patchId, position)` — повертає WR кожного героя на позиції
- [ ] Метод `GetMatchupAsync(heroId)` — WR vs кожного іншого героя
- [ ] Алгоритм scoring: `score(candidate) = WR_in_role + Σ matchup_winrate(candidate, enemy) − penalty_synergy_clash(candidate, ally)`
- [ ] UI: 3 stages — pick first / counter-pick / final adjustment, toggle `Global WR | WR vs enemies`
- [ ] Підсвітка червоним bad picks ("Don't pick Dawnbreaker vs Silencer/Nyx"), зеленим counter'и
- [ ] Кеш у SQLite (matchup matrix актуалізується раз на день)
- [ ] ADR `docs/adr/0004-draft-helper.md`

### Stage 5 — Item recommendations на overlay (A1)
**Deliverable:** ліва панель overlay'ю показує топ-5 рекомендованих айтемів проти конкретної ворожої композиції, оновлюється у real-time.

**DoD:**
- [ ] Stratz метод `GetItemPerformanceVsHeroAsync(heroId)` — WR boost кожного айтема проти конкретного героя
- [ ] Алгоритм: `score(item) = Σ_enemies (winrate_boost(item, enemy) × confidence_weight)`
- [ ] Кеш в SQLite, refresh раз на патч
- [ ] UI: 5 іконок айтемів, tooltip з обґрунтуванням ("Lotus counters Lion/Jakiro: +5.2% WR")
- [ ] Інтеграція в overlay-вікно з Stage 3

### Stage 6 — Повний role-5 playbook
**Deliverable:** повний playbook ролі 5 (5 stages з 0:00 до 30:00), завантажується автоматично коли GSI визначає роль гравця.

**DoD:**
- [ ] `data/playbooks/role5.json` з усіма stage'ами: pre-game, lane 0-10, transition 10-20, mid-game 20-30
- [ ] Role detection через `player.role` GSI поле (якщо є) або через position heuristic
- [ ] Quest HUD у overlay (A2) — показує 1-3 активні квести з прогрес-барами
- [ ] Sound/visual notification на stage transition

## Що **не** входить у MVP
- Imprint.gg інтеграція (чекаємо access)
- Інші ролі (1-4) — після того як role5 пройде user-тестування
- Post-match review (B3) — стане Stage 7
- Авто-апдейтер (Velopack)
- Локалізація крім укр/англ

## Стек (закріплено)
- .NET 8, C# 12
- Avalonia 11.2 (UI + overlay)
- Dota2GSI (antonpup) — GSI receiver
- Stratz GraphQL + OpenDota REST (winrate / matchup / item perf)
- SQLite + Dapper (кеш)
- Rx.NET (потік GameState)
- xUnit (тести core-логіки)

## Структура папок (цільова)
```
src/
  D2Helper.Core/        # GSI, quest engine, draft engine, item engine, Rx pipelines
  D2Helper.Data/        # OpenDota, Stratz, Imprint clients, SQLite cache
  D2Helper.UI/          # Avalonia companion window (B)
  D2Helper.Overlay/     # Avalonia transparent overlay (A)
  D2Helper.Tests/       # xUnit
data/
  playbooks/            # role*.json
  zones.json            # хардкод координат камп/ліній/T1
  constants/            # submodule odota/dotaconstants
secrets/                # gitignored
docs/
  adr/                  # architectural decision records
```

## Конвенції
- Код англійською, doc-comments українською.
- Будь-яка нова фіча → ADR-файл `docs/adr/NNNN-short-title.md`.
- Будь-який зовнішній HTTP-виклик → через `Polly` retry + кеш у SQLite.
- Жодних секретів у репо — все через `secrets/tokens.env` (gitignored).
- Real-time handler GSI має відпрацьовувати за <10ms.
