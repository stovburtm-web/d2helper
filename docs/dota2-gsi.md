# Dota 2 Game State Integration (GSI)

## Що це
GSI — офіційний механізм Valve для отримання стану гри в реальному часі **без** читання пам'яті процесу (тобто **безпечно для VAC/анти-чіт**). Гра сама надсилає JSON HTTP POST запити на локальний сервер, який запускає наш додаток.

Це **єдиний легітимний спосіб** отримувати real-time дані з Dota 2 на стороні клієнта.

## Як підключитись

### 1. Створити конфіг-файл
Шлях:
```
<Steam>/steamapps/common/dota 2 beta/game/dota/cfg/gamestate_integration/gamestate_integration_<name>.cfg
```

Приклад вмісту:
```
"D2Helper Integration Configuration"
{
    "uri"          "http://localhost:3000/"
    "timeout"      "5.0"
    "buffer"       "0.1"
    "throttle"     "0.1"
    "heartbeat"    "10.0"
    "data"
    {
        "auth"            "1"
        "provider"        "1"
        "map"             "1"
        "player"          "1"
        "hero"            "1"
        "abilities"       "1"
        "items"           "1"
        "events"          "1"
        "buildings"       "1"
        "league"          "1"
        "draft"           "1"
        "wearables"       "1"
        "minimap"         "1"
        "roshan"          "1"
        "couriers"        "1"
        "neutralitems"    "1"
    }
}
```

### 2. Запустити локальний HTTP-сервер
Слухає `POST /` на вказаному порту. Гра шле JSON ~10 раз/сек, коли клієнт активний.

### 3. Тікі-частота
- `buffer` 0.1 = група подій в межах 100ms
- `throttle` 0.1 = не частіше 10 req/sec
- `heartbeat` 10 = шле "пусті" пакети раз на 10с, щоб додаток знав, що зв'язок живий

### 4. Adminка
Якщо URI **не** `localhost`, потрібні адмін-права (через `http.sys` на Windows).

## Що віддає GSI (повна структура)

```
GameState
├── Auth                (token для перевірки що це твій конфіг)
├── Provider            (name, appid, version, timestamp)
├── Map                 (matchID, gametime, clocktime, day/night, scores, roshan_state, ...)
├── Player              (LocalPlayer + Teams[]: name, kills/deaths/assists, gold, gpm/xpm, networth, hero_damage, ...)
├── Hero                (LocalPlayer + Teams[]: id, name, level, hp/mana, alive, respawn, buybackcost, статуси: silenced/stunned/hexed/magicimmune/has_aghs_scepter/has_aghs_shard, talent_tree, attributes)
├── Abilities           (для кожного героя: ability name, level, cooldown, can_cast, charges, is_ultimate)
├── Items               (Inventory[6] + Stash[6] + Teleport + Neutral: name, charges, cooldown, can_cast)
├── Events[]            (kill, bounty pickup, tip, що нещодавно сталось)
├── Buildings           (RadiantBuildings/DireBuildings: tower/rax/ancient → health, max_health)
├── League              (для турнірів: league_id, series_id, teams, stream URLs)
├── Draft               (active_team, pick id, remaining_time, picked heroes per team) — фаза піків
├── Wearables           (косметика)
├── Minimap             (всі точкові елементи на мінікарті: name, location, team, vision_range)
├── Roshan              (health, is_alive, spawn_phase, time_remaining, drops)
├── Couriers            (per courier: health, location, items, has_flying_upgrade)
├── NeutralItems        (tier inventory + які вже випали)
└── Previously          (попередній стейт — для діффінгу)
```

## Обмеження
- **Граючи (не спектатор):** доступні лише дані локального гравця та публічні дані (мапа, будівлі, події).
- **Спектатор/режим демки:** доступно повністю по обох командах.
- **Не показує:** позиції ворожих героїв поза lineof-sight, ворожий інвентар, ворожі кулдауни (поза CV).
- Це обмеження Valve, обійти **не можна** легально.

## Корисні бібліотеки

| Мова | Репо | Опис |
|------|------|------|
| C# | [antonpup/Dota2GSI](https://github.com/antonpup/Dota2GSI) | Найбільш зріла, авто-генерація cfg, granular events (TowerDestroyed, ItemAdded, …) |
| C# (приклад) | [pjmagee/dota2-helper](https://github.com/pjmagee/dota2-helper) | Real-world helper з оверлеєм, .NET |
| Node.js | [xzion/dota2-gsi](https://github.com/xzion/dota2-gsi) | Класична JS-обгортка |
| TypeScript | [osztenkurden/dota2gsi](https://github.com/osztenkurden/dota2gsi) | TS-типізована |
| TypeScript | [dotabod/backend](https://github.com/dotabod/backend) | Великий продакшн-проєкт (Twitch tool) |
| Rust | [tomasfarias/dota-gsi](https://github.com/tomasfarias/dota-gsi) | Tokio-based, async |
| Python | [Daniel-EST/dota2gsipy](https://github.com/Daniel-EST/dota2gsipy) | Простий приклад |

## Що НЕ дає GSI (треба брати з інших джерел)
- Історію матчів гравця → **Steam Web API** / **OpenDota API** / **Stratz API**
- MMR/ранги опонентів → Stratz / OpenDota
- Win-rate героїв в патчі → OpenDota, Dotabuff (скрейп або API)
- Counter-pick таблиці / item builds → готувати свою БД (з парсингом dotabuff/stratz)
