# Overwolf — огляд платформи та API

## Що таке Overwolf
Overwolf — фреймворк для написання **in-game overlay додатків** для PC-ігор. Складається з:
1. **Overwolf клієнт** (нативний шар, ставиться користувачу окремо).
2. **Game Events Provider (GEP)** — оверволф сам читає дані гри (для Dota 2 — обгортає GSI + має додаткові події через `gep-package`) і віддає в твій додаток.
3. **Overwolf SDK** — JS/TS API (`overwolf.*`) для вікон, оверлею, hotkeys, IO, hardware-acceleration.
4. **Дистрибуція** через Overwolf App Store.

Технічно додаток — це **HTML + JS/TS** (раніше Chromium-обгортка), нині також **ow-electron** (їхній форк Electron з підтримкою overlay-вікон поверх ексклюзивно-fullscreen ігор).

## Плюси Overwolf для нашого кейсу
- Готовий рендер оверлея поверх Dota 2 (включаючи fullscreen exclusive).
- Hotkeys, capture, IPC, autoupdate — з коробки.
- Готова обгортка GSI + додаткові events (їхній `gep-package` для Dota 2).
- Магазин = розповсюдження + монетизація (ads/subscriptions).

## Мінуси Overwolf для нашого кейсу
- Користувач **зобов'язаний** ставити Overwolf-клієнт (це окрема програма ~200MB).
- JS/TS-стек — повільніше обробка важких даних (наприклад, real-time скоринг шмоток проти 5 героїв за 16ms кадру).
- Магазин = частка прибутку Overwolf, реклама.
- Менше контролю над процесом.
- Прив'язка до їхнього lifecycle/manifest-формату.

## Ключові репо Overwolf (https://github.com/overwolf)
| Репо | Що там |
|------|--------|
| [overwolf/types](https://github.com/overwolf/types) | TypeScript типи для всього `overwolf.*` API |
| [overwolf/events-sample-app](https://github.com/overwolf/events-sample-app) | Робочий sample app з Game Events (TS) — стартова точка |
| [overwolf/overwolf-api-ts](https://github.com/overwolf/overwolf-api-ts) | Утиліти/враппери поверх API |
| [overwolf/ow-electron-packages-sample](https://github.com/overwolf/ow-electron-packages-sample) | Sample на новому стеці **ow-electron** (Electron + overlay) |
| [overwolf/ow-electron-packages-types](https://github.com/overwolf/ow-electron-packages-types) | Типи для ow-electron пакетів (зокрема `@overwolf/ow-electron-packages-types/overlay`, `gep`) |
| [overwolf/overwolf-plugins](https://github.com/overwolf/overwolf-plugins) | C# плагіни-DLL, які можна викликати з JS (для важких/нативних задач) |
| [overwolf/gep-sim](https://github.com/overwolf/gep-sim) | Симулятор Game Events для розробки без запущеної гри |
| [overwolf/community-gists](https://github.com/overwolf/community-gists) | Прикладні снипети |

## Що Overwolf дає для Dota 2 окремо
Документація: `https://overwolf.github.io/api/games/events/dota-2`

Категорії подій (від `overwolf.games.events`):
- **match_info**: matchid, players, draft, day/night, score, …
- **roshan**: state, respawn timer
- **hero**: level, position, hp, mana, items, abilities
- **kill** events
- **buildings**
- **runes**

Під капотом — той самий GSI + парсинг log-файлів. Все, що є в GSI, доступне через `overwolf.games.events.onInfoUpdates2` / `onNewEvents`.

## Висновок щодо Overwolf для D2Helper
**Не використовуємо Overwolf як платформу.** Причини:
1. Нам не потрібен їхній магазин/реклама.
2. Хочемо нативну швидкість для важкої логіки (counter-pick scoring, ML-моделі для рекомендацій).
3. GSI підключається напряму без Overwolf — нема dependencies.

**АЛЕ:** беремо звідти ідеї та UX-патерни:
- Структуру `events-sample-app` як референс.
- `gep-sim` як ідею **симулятора станів** для тестів.
- ow-electron — якщо в майбутньому захочемо легкий overlay через web-стек.
