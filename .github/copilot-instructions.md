# D2Helper — інструкції агента (Copilot/Claude)

## Product north star (важливо)
**Ідея, з якою агент має звіряти кожне рішення:**
> Більшість гравців до 4k mmr не розуміють що робити в конкретну хвилину гри.
> Вони сидять на лайні до 15-ї, не йдуть під руни, не стекають, не ротейтять
> на 6-8 хвилині. Базові муви відомі (рунотайм 2:00/4:00, стек xx:53,
> wisdom 7/14/21, lotus +3 хв, ротація 5-ки на 6-8 хв, тощо), але гравець
> їх **не відчуває в потоці гри**.
>
> D2Helper **гейміфікує** ці муви: подає їх як **квести-нагадування** в
> in-game overlay, які закриваються коли гравець зробив правильну дію.
> Кожен виконаний квест — це +0.5...1% до шансу виграти матч.
> Це не статистика і не post-game аналіз — це **активний коучинг real-time**.

**Слідство для архітектури:**
- Quest = ціль на конкретний таймінг (`fire_at_clock`, `due_at_clock`) + умова виконання (Denies≥N, LastHits≥N, picked_lotus, picked_rune, …).
- Quests живуть у **overlay** поверх Dota, **не** в companion-window.
- Показуємо **≤3 активних квести** одночасно. Виконаний квест залишається з міткою ✅ доки наступний по таймлайну не витисне його.
- Playbook = впорядкований по часу набір квестів для конкретної ролі (P1..P5).
- Не дублювати те, що Dota показує сама (Roshan/Tormentor таймери — вже є нативно).

## Контекст проєкту
**D2Helper** — десктопний помічник для гравців у Dota 2 на Windows.
Не просто статистика (як Overwolf Dota Plus), а **активні підказки під час гри**:
- counter-pick під час драфту;
- рекомендації по шмотках/розкачці проти конкретних ворожих героїв в поточному патчі;
- тайминг-квести (руни, lotus, wisdom, stack, ротація);
- аналіз опонентів (ранг, головні герої).

## Стек (поки що рекомендований, остаточно затвердити з користувачем)
- **.NET 8**, C# 12
- **Avalonia 11** для головного UI (можливо WPF — підтвердити)
- **GameOverlay.Net** (Direct2D) для in-game overlay поверх Dota 2
- **Dota2GSI** (antonpup) — NuGet, обгортка над GSI
- **SQLite** + **Dapper** для кешу патчів/матчапів
- **Rx.NET** для потоку GameState
- **Polly** для retry зовнішніх HTTP
- **xUnit** для тестів
- **Velopack** для авто-апдейтів

## Принципи
1. **Real-time first.** GSI прилітає ~10 Hz. Будь-який handler має відпрацьовувати за <10ms. Тяжке — в фон, з кешем.
2. **Безпека для VAC.** Ніякого читання пам'яті Dota 2, ніяких DLL-інжектів. Тільки GSI + Steam/OpenDota API.
3. **Offline-first.** Усе, що можна, кешуємо в SQLite. Без інтернету додаток має працювати на закешованих даних.
4. **Не тягнути Overwolf як залежність.** Свій оверлей (`GameOverlay.Net` або WPF transparent topmost).
5. **Domain language укр/англ.** Код англійською, UI-тексти й коменти doc — українською (бо це мова продукту).
6. **Тестованість.** `D2Helper.Core` (Engine, Bus, GSI парсер) — без UI-залежностей, повністю unit-test'абельний. Симулятор GSI (схожий на `overwolf/gep-sim`) — обов'язковий артефакт.

## Файлова конвенція
- `src/D2Helper.Core/` — domain, GSI, engine. **Жодних WPF/Avalonia using'ів тут.**
- `src/D2Helper.Data/` — БД, HTTP-клієнти.
- `src/D2Helper.UI/` — Avalonia.
- `src/D2Helper.Overlay/` — лише render-логіка оверлею.
- `data/constants/` — submodule `github.com/odota/dotaconstants`.
- `docs/` — markdown'и опису API, архітектури, рішень (ADR).

## Що агент має робити проактивно
- Перед написанням коду, який торкається GSI-даних — звіряти з `docs/dota2-gsi.md`.
- Будь-яка нова фіча → ADR-файл в `docs/adr/NNNN-title.md`.
- Не вигадувати поля GSI, яких нема в антонпуповській бібліотеці. Якщо потрібне поле відсутнє — це сигнал перевірити Valve-доку, не fallback на memory-read.
- Не пропонувати рішення, що ламають Steam ToS (скрейп Dotabuff, інжекти в процес Dota 2).
- Для зовнішніх API — поважати rate-limit (OpenDota 60/min на free tier), реалізовувати backoff.

## Чого агент НЕ має робити
- Не використовувати Overwolf SDK (вирішення відмовитись задокументовано в `docs/overwolf.md`).
- Не пропонувати Electron/TypeScript як основний стек.
- Не читати пам'ять гри / не патчити .dll Дотиних бінарників.
- Не комітити OpenDota/Stratz API-ключі.
- Не створювати markdown-документацію по змінах коду без явного запиту.

## Корисні референси
- `docs/dota2-gsi.md` — структура GSI, як підключитись.
- `docs/overwolf.md` — чому не Overwolf, але що звідти беремо.
- `docs/external-apis.md` — OpenDota / Stratz / Steam.
- `docs/architecture.md` — модулі, схема, файлова структура.
- https://github.com/antonpup/Dota2GSI — основна GSI-бібліотека.
- https://github.com/pjmagee/dota2-helper — реальний приклад helper'а на C#.
- https://github.com/odota/dotaconstants — id↔name мапінги, JSON.
- https://docs.opendota.com/ — API.

## Команди (поки заглушки, доповнити при ініціалізації проєкту)
- Build: `dotnet build`
- Test: `dotnet test`
- Run: `dotnet run --project src/D2Helper.UI`
- Pack overlay: TBD

## Status
🟡 **Pre-init.** Проєкт ще не ініціалізовано (нема `.sln`). Перший крок — створити solution + проєкти за структурою з `docs/architecture.md`, додати NuGet `Dota2GSI`, написати echo-сервер що логує `NewGameState`.
