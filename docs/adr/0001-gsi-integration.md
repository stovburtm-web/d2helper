# ADR 0001 — GSI integration

## Status
Accepted — Stage 1.

## Context
Уся real-time логіка D2Helper (quest tracker, overlay timings, draft phase detection, item recs тригер) залежить від потоку GameState з Dota 2. Valve офіційно дає це через **Game State Integration** — конфіг-файл у папці гри змушує Dota постити JSON по HTTP кожен ~100ms.

Альтернативи:
- Memory reading — заборонено VAC ToS, **відкинуто**.
- DLL injection — те саме, **відкинуто**.
- Replay parsing — тільки post-game, не підходить для real-time.

## Decision
Використовуємо бібліотеку [`antonpup/Dota2GSI`](https://github.com/antonpup/Dota2GSI):
- зрілий C# wrapper з типізованою моделлю `GameState`
- сама піднімає HTTP listener
- має утиліту `GenerateGSIConfigFile` що пише cfg у правильне місце

Поверх неї будуємо тонкий `GameStateBus` як `IObservable<GameState>` (Rx.NET) щоб всі підписники (quest engine, overlay, draft helper) могли реактивно реагувати без поллінгу.

### Конкретика
- **Порт:** `:3000` (стандарт для GSI examples)
- **CFG-файл:** `gamestate_integration_d2helper.cfg` у `Steam/steamapps/common/dota 2 beta/game/dota/cfg/gamestate_integration/`
- **Throttling:** GSI шле ~10 Hz. Bus буферизує останній стан, підписники самі вирішують throttle/sample.
- **Reconnect:** якщо процес Dota перезапустився — listener виживає, просто чекає на наступний POST.
- **Diff detection:** Rx `DistinctUntilChanged` по timestamp щоб не дублювати stage-emits.

## Consequences
- ➕ Жодного memory-read → VAC-safe.
- ➕ Типізована модель — компіляція ловить помилки.
- ➕ Rx-pipeline = легко тестувати (фідимо тестові payload'и).
- ➖ GSI не дає ворожі кулдауни / інвентар → деякі фічі лімітовані до "manual tracker" (Stage 6+).
- ➖ Користувач має одноразово погодитись на створення cfg-файла; додаток має graceful fallback "GSI cfg missing → please run setup".

## Implementation checklist
1. `dotnet add package Dota2GSI` у `D2Helper.Core`.
2. `D2Helper.Core/Gsi/GameStateBus.cs` — Singleton, виставляє `IObservable<GameState>`.
3. `D2Helper.Core/Gsi/GsiSetup.cs` — `EnsureConfigExists()`: знаходить Dota cfg-папку, пише cfg якщо нема.
4. `D2Helper.UI` показує статус-рядок: `GSI: connected` / `GSI: waiting for Dota`.
5. Юніт-тест: фідимо JSON-payload в bus, перевіряємо що підписник отримав `GameState` з правильним `map.clock_time`.

## References
- [Valve GSI docs](https://developer.valvesoftware.com/wiki/Counter-Strike:_Global_Offensive_Game_State_Integration) (CS doc, але формат той же)
- `docs/dota2-gsi.md` — наш конспект
- https://github.com/antonpup/Dota2GSI
