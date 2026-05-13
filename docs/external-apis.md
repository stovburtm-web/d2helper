# Зовнішні API (поза GSI)

GSI дає тільки real-time стан матчу. Для "розумних" підказок (counter-pick, шмотки в патчі, історія опонентів) потрібні зовнішні джерела.

## Steam Web API (офіційне Valve)
- Docs: https://steamapi.xpaw.me/ + https://wiki.teamfortress.com/wiki/WebAPI
- Ключ: https://steamapi.xpaw.me/ (треба Steam-акаунт)
- Endpoints для Dota 2: `GetMatchHistory`, `GetMatchDetails`, `GetHeroes`, `GetGameItems`, `GetLiveLeagueGames`, `GetPlayerSummaries`.
- **Мінус:** rate limit 100k/day, дані сирі, без агрегацій (треба самим рахувати win-rate тощо).
- **Плюс:** офіційне, безкоштовне, стабільне.

## OpenDota API
- Docs: https://docs.opendota.com/
- Безкоштовний tier — 60k запитів/міс, 60 req/min. Платний — від $5/міс.
- Має все: матчі, профілі, win-rate героїв по патчах, counter-pick матриці, мета-білди, бенчмарки.
- Endpoints для нас:
  - `/heroes/{hero_id}/matchups` → win-rate проти кожного героя
  - `/heroes/{hero_id}/itemPopularity` → популярні шмотки на героя
  - `/players/{account_id}` → профіль опонента (rank tier, recent matches)
  - `/players/{account_id}/heroes` → на яких героях він/вона грає, win-rate
  - `/constants/items`, `/constants/heroes`, `/constants/patchnotes`
- **Найкраще джерело** для нашої задачі.

## Stratz API
- Docs: https://docs.stratz.com/
- GraphQL. Безкоштовно з обмеженням.
- Глибша аналітика: build paths, lane outcomes, ML-передбачення.
- MMR/medal оцінки опонентів.

## Dotabuff
- Публічного API нема. Тільки скрейп — **не робимо**, проти ToS.

## Patch / item / hero дані локально
Замість API-запитів кожного матчу — **кешуємо** в SQLite:
- Hero matchup-матриця (121×121 win-rate) — раз на тиждень обновлюємо з OpenDota.
- Hero item popularity per patch.
- Constants з https://github.com/odota/dotaconstants (JSON-файли, оновлюються по патчах).

## Steam-логи матчу (replay)
- Файли `.dem` лежать в `replays/`. Парсити можна через `clarity` (Java) або `manta` (Go).
- Для real-time допомоги **не потрібно** — replay доступний тільки після матчу.

## Що використовуємо у D2Helper
| Джерело | Що беремо | Коли |
|---------|-----------|------|
| **GSI (локальний)** | поточний стан матчу | real-time, ~10 Hz |
| **OpenDota REST** | matchup-матриці, item popularity, профілі опонентів | в фазі draft + раз на старті матчу |
| **dotaconstants** (git) | id↔name мапінги, описи шмоток, абілок | оновлюємо при старті, кешуємо |
| **Stratz GraphQL** *(опційно)* | глибші metric'и, ranked tier | в фазі draft |
| **Steam Web API** | fallback для базових даних | при відсутності openDota-ключа |
