# D2Helper — архітектура та вибір стеку

## Вимоги
1. Windows PC десктоп-додаток.
2. Real-time робота під час матчу: low-latency обробка GSI потоку.
3. Тяжкі обчислення: counter-pick scoring, item-build evaluation, рекомендації по drafт'у.
4. Оверлей поверх гри (поверх Dota 2 fullscreen exclusive).
5. Локальна БД (кеш патчів, історія користувача).
6. HTTP-клієнти до OpenDota/Stratz.
7. Простота розповсюдження (інсталятор, авто-апдейт).

## Вибір мови — порівняння

| Стек | Перформанс | Overlay поверх fullscreen | Ekosystema GSI | DX | Розмір .exe | Вердикт |
|------|-----------|---------------------------|----------------|-----|-------------|---------|
| **C# / .NET 8 + WPF або Avalonia** | Дуже добрий (JIT, span, SIMD) | Так (через DXGI/WS_EX_LAYERED + topmost click-through) | **`Dota2GSI` (антонпуп)** — best-in-class | Високий | ~80MB self-contained | ✅ **Рекомендую** |
| **Rust + Tauri / egui** | Топ | Так, але треба самим робити overlay-вікно | Є `tomasfarias/dota-gsi`, але менш зріла | Середній (стрімкіша крива) | ~15MB | Хороша альтернатива якщо хочемо max-перф |
| **C++ / Qt** | Топ | Так | Самим писати (HTTP-сервер на boost.beast / cpp-httplib) | Низький | ~50MB | Overkill для нашої задачі |
| **TypeScript + Electron / ow-electron** | Слабкий для обчислень | Через Overwolf — ок | `dotabod`, `xzion/dota2-gsi` | Дуже високий | ~150MB | ❌ Не підходить (повільно, важко) |
| **Go** | Добрий | Слабка GUI-екосистема на Win | Власна імплементація | Середній | ~20MB | Можна, але GUI-біль |
| **Python** | Поганий | Слабко | `dota2gsipy` | Високий | Великий + інтерпретатор | ❌ Не підходить для real-time |

### Рекомендація: **C# / .NET 8 + Avalonia**
**Чому:**
- `Dota2GSI` (antonpup) — найзріліша GSI-бібліотека з готовими granular events (TowerDestroyed, ItemAdded, …). Економить тижні роботи.
- .NET 8 — fast JIT, чудовий `System.Text.Json`, NativeAOT якщо треба ще швидше та менше .exe.
- **Avalonia** замість WPF — кросплатформ + сучасніший XAML + швидший рендер (Skia). Якщо потім захочемо Linux — нічого не переписуємо.
- HttpClient + Polly для retry до OpenDota.
- SQLite + EF Core (або Dapper) для кешу.
- `pjmagee/dota2-helper` — готовий референс real-world помічника на C#.
- Оверлей: окреме `TransparentWindow` з `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST`, click-through. Або готова бібліотека [GameOverlay.Net](https://github.com/michel-pi/GameOverlay.Net) для прямого D2D рендеру.

### Альтернатива: **Rust + egui/iced + tokio**
Бери, якщо:
- хочеш SQLite/HTTP/GSI в одному бінарнику ~15MB
- готовий писати GSI-парсер з нуля (або базуватись на `tomasfarias/dota-gsi`)
- цінуєш memory safety і відсутність GC stutter'ів

## Архітектура (модулі)

```
┌──────────────────────────────────────────────────────────────────┐
│                       D2Helper Desktop App                       │
│                                                                  │
│  ┌────────────────┐    ┌──────────────────┐   ┌──────────────┐  │
│  │  GSI Server    │───▶│  GameState Bus   │──▶│  UI / Overlay│  │
│  │ (HttpListener  │    │  (Rx.NET / event │   │  (Avalonia + │  │
│  │  on :3000)     │    │   stream, diffs) │   │  GameOverlay)│  │
│  └────────────────┘    └────────┬─────────┘   └──────────────┘  │
│                                 │                                │
│                        ┌────────▼──────────┐                     │
│                        │  Advice Engine    │                     │
│                        │  - draft scorer   │                     │
│                        │  - item recom.    │                     │
│                        │  - timing alerts  │                     │
│                        └────────┬──────────┘                     │
│                                 │                                │
│            ┌────────────────────┼────────────────────┐           │
│            ▼                    ▼                    ▼           │
│  ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐  │
│  │ Patch / Hero DB  │ │ OpenDota Client  │ │ Stratz Client    │  │
│  │ (SQLite, refresh │ │ (HttpClient +    │ │ (GraphQL)        │  │
│  │  weekly)         │ │  Polly retry)    │ │                  │  │
│  └──────────────────┘ └──────────────────┘ └──────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

### Модулі
1. **GSI.Server** — HttpListener на 127.0.0.1:3000, парс JSON у DTO, push в Bus.
2. **GameState.Bus** — Reactive stream (Rx.NET `Subject<GameState>`). Підраховує діффи (`Previously` vs `Current`) і емітує high-level events: `EnemyHeroLevelUp`, `EnemyBoughtBKB`, `RoshanRespawnSoonTM`, etc.
3. **Advice.Engine** — підписаний на Bus + має доступ до Patch DB. Видає рекомендації:
   - У draft: counter-pick scoring (зважена сума win-rate проти ворожих піків).
   - In-game: "купи Lotus проти Lina/Sniper", "BKB зараз ефективний бо ворог має 3+ stun".
   - Timing: "Roshan через 2 хв, у тебе нема смоку".
4. **PatchDB** — SQLite. Таблиці: heroes, items, abilities, hero_matchups (hero_a, hero_b, winrate, sample_size), hero_item_winrate (hero, item, winrate, patch).
5. **OpenDotaClient / StratzClient** — async HTTP з rate-limit, кешем на диску.
6. **UI**:
   - Main window (Avalonia) — налаштування, історія, профіль.
   - Overlay window (GameOverlay.Net D2D) — поверх гри: timers, advice cards, hotkey-toggle.
7. **Hotkeys** — глобальні (через RegisterHotKey WinAPI) для toggle overlay.
8. **Updater** — Velopack / Squirrel.

## Файлова структура (запропонована)
```
d2helper/
├── docs/                    ← оце все
├── src/
│   ├── D2Helper.Core/       ← GSI, GameState, Bus, Engine (no UI)
│   ├── D2Helper.Data/       ← SQLite, OpenDota/Stratz clients
│   ├── D2Helper.UI/         ← Avalonia app
│   ├── D2Helper.Overlay/    ← GameOverlay.Net рендер
│   └── D2Helper.Tests/
├── data/
│   └── constants/           ← dotaconstants JSON (зкомітимо як submodule)
├── .agents/                 ← інструкції для AI-агентів
└── D2Helper.sln
```

## Питання для уточнення (треба тобі вирішити)
1. Підтримка **тільки Windows** чи готувати Linux/Mac в перспективі?
2. **Avalonia** чи **WPF**? (Avalonia рекомендую за кроссплат + сучасність.)
3. Готовий тримати **OpenDota API key**? (Безкоштовний tier має ліміти — 60 req/min.)
4. Чи треба **історія гравця** (трекати свої матчі) чи лише real-time помічник?
5. Які саме категорії підказок — пріоритет (драфт, шмотки, тайминги, фарм-маршрути)?
