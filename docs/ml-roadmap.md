# ML Roadmap — D2Helper Danger Model

> **Статус:** план затверджений до реалізації після завершення V1.2 збору.
> Дата: 2026-05-15. Поточний патч Dota: **7.41c**.

## TL;DR
- **Stratz GraphQL** має `players[].playbackData.playerUpdatePositionEvents` (1 Hz per-player) + `deathEvents`, `killEvents`, `playerUpdateHealthEvents`, `playerUpdateGoldEvents`. Підтверджено живим запитом.
- Це означає можна побудувати **повноцінний supervised dataset**: для кожної секунди гри маємо state всіх 10 гравців → знаємо хто помре у наступні N секунд.
- Тренуємо лежну модель (XGBoost / LightGBM) яка для cell (x,y) і player state передбачає `P(death у наступні 15 сек)`.
- Експертна модель користувача (minimap-aware V1.3) залишається як **feature engineer + fallback** для cold start.

---

## 1. Цільова задача

```
input:  state_at_t = {
    side: Radiant|Dire,
    time_sec: int,
    my_pos: (x, y),
    my_hp_pct: float, my_mana_pct: float, my_gold: int,
    ally_positions: [(x,y) × 4],     // що бачить minimap
    visible_enemies: [(x,y, hp_pct) × 0..5],
    creep_wave_positions: [(lane, x, y) × N],
    last_seen_enemies: [(x,y, secs_ago) × 0..5]
}
output: heatmap[64x64] = P(death(my_team) in next 15s | piece at cell)
```

Це **regression на grid**, але простіше формулюємо як **per-sample classification**:
для кожного гравця в кожну секунду:
- features = state_at_t + future_position_at_(t+5)  
- label = `died_at(t..t+15)` ∈ {0, 1}

→ binary classifier на ~1M-10M рядках з 1000-10000 матчів.

## 2. Що дає Stratz (підтверджено)

`Match.players[].playbackData`:
| Field | Hz | Корисність |
|---|---|---|
| `playerUpdatePositionEvents { time, x, y }` | ~1 Hz | **CORE** — траєкторії всіх 10 гравців |
| `playerUpdateHealthEvents { time, healthPerSec ... }` | event-based | features для рисик-сигналу |
| `playerUpdateGoldEvents` | ~30 sec | networth leads |
| `playerUpdateLevelEvents` | event-based | level diff |
| `killEvents { time, target, ...}` | event | groundtruth |
| `deathEvents { time, target }` | event | **labels** |
| `runeEvents { time, x, y, runeType }` | event | counter-feature (rune timings) |
| `wardEvents { time, x, y, wardType }` | event | vision features |
| `inventoryEvents` | event | item state |

Coords system: 128×128 grid (x,y ∈ ~64..192), той самий що OpenDota `deaths_pos`. ✅ сумісно з V1.2.

## 3. Архітектура data pipeline

```
[Stratz GraphQL] → [D2Helper.Knowledge.Cli stratz-scrape]
                        ↓
                  [data/stratz-matches/*.json.gz]   ← raw match dumps, gitignored
                        ↓
                  [D2Helper.Knowledge.Cli build-dataset]
                        ↓
                  [data/training-set.parquet или .csv]
                  rows: ~1M (1000 матчів × 10 гравців × 100 sec sample)
                        ↓
                  [Python: train.py — XGBoost/LightGBM]
                        ↓
                  [models/danger-7.41c-v1.onnx]    ← export to ONNX
                        ↓
                  [D2Helper.Vision: ONNX runtime → ComputeDanger()]
```

**Чому ONNX:** Microsoft.ML / ML.NET вміють тренувати, але важка дебагінг. Stack для DS — Python + xgboost + sklearn — стандарт. ONNX експорт → Avalonia додаток вантажить через `Microsoft.ML.OnnxRuntime` (~5ms inference на frame).

## 4. Phased roadmap

### Phase A — Stratz scraper (1-2 дні роботи)
- **A1**: Розширити `StratzClient`: `GetMatchPlaybackAsync(id)` повертає raw `JsonDocument`. Поля з пункту 2.
- **A2**: `D2Helper.Knowledge.Cli stratz-fetch --limit 1000 --mmr 3000-6000 --eu` — використовує OpenDota SQL (вже є) для list of match_ids, потім fetch'ить через Stratz. Зберігає gz JSON у `data/stratz-matches/`.
- **A3**: Перевірка квот Stratz (free tier: ~250 req/hour, paid: 5000/hour). Підрахунок: 1000 матчів = ~4 години на free tier. Прийнятно.

### Phase B — Feature extraction (1-2 дні)
- **B1**: `DatasetBuilder.cs` — читає raw match JSON, генерує rows:
  ```csharp
  record TrainingRow(
      int MatchId, int PlayerSlot, int TimeSec,
      float X, float Y, float HpPct, float ManaPct, int Gold,
      float[] AllyDistances,        // 4 distances to allies
      int VisibleEnemies, int UnseenEnemies,
      float NearestEnemyDist,
      float CreepFrontDelta,         // деформація лайн-equilibrium
      bool DiedInNext15Sec);          // ← LABEL
  ```
- **B2**: Sample кожну 5-ту секунду (sub-sampling) щоб скоротити обсяг. 1000 матчів × 10 players × 40min / 5s = **480k рядків** — comfortable.
- **B3**: Export to `.csv` (простіше за parquet для першого ітерування).

### Phase C — ML training (1 день)
- **C1**: Python script `tools/train_danger.py` — pandas + xgboost + sklearn.
  ```python
  X, y = load_csv('data/training-set.csv')
  Xtr, Xte, ytr, yte = train_test_split(X, y, stratify=y)
  m = xgb.XGBClassifier(max_depth=6, n_estimators=500)
  m.fit(Xtr, ytr)
  print(classification_report(yte, m.predict(Xte)))
  ```
- **C2**: Експорт ONNX: `onnxmltools.convert_xgboost(m)`.
- **C3**: Baseline target: AUC > 0.75 на 15-сек horizon. (Random = 0.5, експертні правила ~0.65.)

### Phase D — Integration (1 день)
- **D1**: Додати `Microsoft.ML.OnnxRuntime` package у `D2Helper.Vision`.
- **D2**: `DangerZoneModel.LoadOnnx(path)`, у `ComputeDanger(state)` робимо batch inference на 64×64 cells.
- **D3**: A/B toggle у settings: пузо ML vs експертна. Користувач може порівняти.

### Phase E — User-personalized fine-tune (later)
- **E1**: GSI state logger пише `data/local-states.db`.
- **E2**: Online retrain на твоїх матчах кожний N games → personal model overlay.

## 5. Інтеграція з V1.3 експертною моделлю

Експертна модель **не викидається**. Вона стає:
1. **Feature engineer** — її output (creep_push_score, ally_safety_score) йде у ML як feature.
2. **Cold-start fallback** — для патчів де ще нема тренованої моделі (новий герой/новий патч).
3. **Sanity check** — якщо ML модель дає danger=0.05 у місці де експертна каже 0.95 (наприклад 5 ворогів видимо в радіусі 500) — warning у логах + downweight ML.

## 6. Secrets & rate limits

- **Stratz token** зберігається в `secrets/stratz.env` (gitignored).
- **D2Helper.Knowledge.Cli** читає token з env: `STRATZ_TOKEN` або з файла `secrets/stratz.env`.
- Stratz free: ~250 req/hour. 1000 матчів = ~4 год. У CLI робимо `delay 14.5 sec` між запитами + кеш у SQLite.
- OpenDota free: 60/min. Уже реалізовано (1.1s delay).

## 7. Що НЕ робимо

- ❌ Не використовуємо OpenDota `playbackData` (там тільки events, не positions — підтверджено через introspection).
- ❌ Не тренуємо neural network з нуля (overkill для tabular features + 1M rows).
- ❌ Не зберігаємо повний JSON matches у git — тільки aggregated parquet/csv.
- ❌ Не хочемо real-time tradeoff inference > 20ms (10 Hz GSI tick).

## 8. Що мірятимемо

- **Coverage:** скільки % смертей у grid визначені >0.5 danger за 10 сек до події. Target: 60%+.
- **Precision @ top-10 cells:** з 10 найнебезпечніших cells на минімапі — скільки реально мали смерть у наступні 15 сек. Target: 30%+.
- **Quest interplay:** скільки разів виконаний quest "не йди в reflex zone" корелює зі змінами P(death).

## 9. Послідовність наступних PR (рекомендована)

1. `feat(vision): integrate V1.2 empirical heatmap into DangerZoneModel` ← наступний крок
2. `feat(data): StratzClient.GetMatchPlaybackAsync`
3. `feat(knowledge): stratz-fetch CLI verb`
4. `feat(knowledge): DatasetBuilder + CSV export`
5. `tools(ml): python train_danger.py + ONNX export`
6. `feat(vision): ONNX-based danger inference + A/B toggle`
7. `feat(quests): minimap-aware V1.3 (expert rules) as features into ML`

---

**Status:** план затверджено. Виконуємо після підключення V1.2 у DangerZoneModel.
