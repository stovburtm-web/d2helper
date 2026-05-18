# Role 5 (Hard Support) — Quest playbook DRAFT

> Чорновий список таймінг-квестів. Заточений під low MMR (<4k):
> акцент на **базові муви які гравці не роблять**, не на нюанси.
> Кожен квест має тригер, ціль і умову закриття. Все по in-game clock (game_time, не real time).
>
> Формат: `[T] ID — Title // trigger / due / done_when / rationale`
> T = severity: **!** обов'язковий core / **·** quality-of-life / **?** опційний.
>
> Edit freely. Видаляй що зайве, додавай свої формулювання.

---

## A. Pre-game / Lane setup (−1:30 ... +0:00)

`[!] pre_courier_ward` — **"Купити Observer + Sentry + Tango перед нулем"**
- fire_at: -1:30 (strategy time)
- due_at: -0:10
- done_when: `Items` містить `ward_observer >= 1` AND `ward_sentry >= 1`
- rationale: 5-ки які стартують лайн без сентрі — норма в 3к. Дешеве нагадування.

`[!] pre_block_camp` — **"Заблокуй ворожий пул-камп"**
- fire_at: -1:00
- due_at: -0:30
- done_when: позиція в зоні `enemy_pull_camp_T1` between -1:00 і -0:20 ≥ 5с
- rationale: ворог не зможе пуляти першу хвилину = твій керрі отримає 4-5 крипів безпечно.

`[·] pre_obs_high_ground` — **"Поставити обзорку на високій точці лайна"**
- fire_at: -0:30
- due_at: +0:30
- done_when: `wards_placed >= 1` в зоні `safe_lane_high_ground`
- rationale: бачиш чи їх 4-5 у лісі, чи свої руни.

---

## B. Early lane (0:00 ... 5:00)

`[!] rune_2_00` — **"Контроль water-rune 2:00"**
- fire_at: 1:30
- due_at: 2:15
- done_when: `picked_rune` подія в [1:55, 2:15] АБО позиція ≤ 600 від rune-spot у вікні [1:50, 2:10]
- rationale: одна з найважливіших дій. На 4к в 70% ігор саппорти стоять на лайні і пропускають.

`[!] stack_1_53` — **"Стек ancients/pull-camp на :53"**
- fire_at: 1:40 (повтор кожні 60с до 8:53)
- due_at: 1:55 (вікно −05 ... +02с)
- done_when: позиція ≤ 400 від camp_spawn в [1:48, 1:55] АБО неактивний камп у [1:55, 2:00]
- rationale: legacy `xx:53` правило, без нього 5-ка дає нуль фарму керрі.

`[·] sentry_juke_3_00` — **"Сентрі на ворожому пул-кампі ~3:00"**
- fire_at: 2:45
- due_at: 3:30
- done_when: `sentry_placed` в зоні `enemy_jungle_pull_T1`
- rationale: блокає їх пул-камп після того як унблочиться.

`[!] rune_4_00` — **"Контроль water-rune 4:00"**
- fire_at: 3:30
- due_at: 4:15
- done_when: as rune_2_00
- rationale: те саме що 2:00. Особливо важлива якщо у мідера bottle empty.

---

## C. Mid-lane support window (5:00 ... 9:00)

`[!] dewards_5_00` — **"Sweep по можливих ворожих обзорках"**
- fire_at: 5:00
- due_at: 6:30
- done_when: `wards_killed >= 1` АБО `sentries_used >= 1` в [5:00, 6:30]
- rationale: 5 хв — фарм-меблі ворожого 4/5. Снести = вони знов витрачаються.

`[?] rotation_6_8` — **"Підкатись на мід або офф з smoke (6−8 хв)"**
- fire_at: 6:00
- due_at: 8:00
- done_when: ([був на mid-T2 в радіусі 1200 ≥ 10с] OR [був на off-T2 в радіусі 1200 ≥ 10с]) АБО `kill_assist` зафіксовано
- rationale: класичний gank-window. Майже ніхто не ротейтить, бо лайн "дає золото".

`[!] wisdom_7_00` — **"Зібрати wisdom rune"**
- fire_at: 6:30 (повтор +7:00)
- due_at: 7:30
- done_when: `picked_rune` тип `wisdom` в [6:55, 7:30]
- rationale: 200 XP — найдешевший left-behind XP в грі.

---

## D. Mid-game objective window (9:00 ... 16:00)

`[!] lotus_3_00` — **"Lotus pool (+3 хв з 3:00, тобто 3/6/9/12/15...)"**
- fire_at: за 0:30 до spawn (2:30, 5:30, 8:30, ...)
- due_at: spawn + 0:45
- done_when: `picked_lotus` event у вікні [spawn − 0:05, spawn + 0:45]
- rationale: безкоштовні 160 + 160 hp pots. Команди втрачають десятки за гру.

`[!] tormentor_window` — **"Готуйся до tormentor 20:00"** (якщо ще активний)
- fire_at: 18:30
- due_at: 21:00
- done_when: команда вбила tormentor (`tormentor_killed`) АБО aegis aura received
- rationale: tormentor дає team-shard на P1/P5 — gigantic mid-game spike.

`[·] glyph_used` — **"Глиф коли крипи у вежу"**
- fire_at: при `enemy_creep_in_T1_radius` (контекстуальний)
- due_at: +0:08
- done_when: `glyph_used` event у вікні +10с
- rationale: −50 hp на крипа за 8с глифа — спасає T1 у 50% випадків.

`[?] smoke_gank` — **"Один smoke gank до 15:00"**
- fire_at: 12:00 (одноразовий)
- due_at: 15:00
- done_when: будь-який team-smoke (`smoke_used` де ≥2 алі поруч) у вікні
- rationale: будиш всю команду на 1 ротацію — частий джек-пот.

---

## E. Reactive quests (не по таймеру, по подіях)

`[!] react_low_hp_retreat` — **"У тебе <30% хп — назад"**
- trigger: `hp_pct < 30` continuously for 5s AND `nearest_enemy < 1500`
- done_when: hp_pct > 50 АБО відстань > 2000 від ворога
- cooldown: 60с
- rationale: саппорти 5-ки годують ворога коли тиснуть hp.

`[!] react_no_buyback` — **"Без байбеку — не лізь"**
- trigger: `game_time > 18:00` AND `gold < buyback_cost` AND `position in enemy_jungle/lanes_T2+`
- done_when: повернувся в свою половину мапи АБО купив байбек-золото
- rationale: дороге саппортне життя 18+ хв = втрачений aegis/tormentor для команди.

`[·] react_courier_used` — **"Кур'єр на тобі довше 60с — відправ або делівер"**
- trigger: `courier.owner == self AND courier.idle > 60s AND has_items_to_deliver`
- done_when: courier delivery initiated
- rationale: 4-ка/керрі чекають на TP/смок який лежить у саппорта в інвентарі.

---

## F. Late game (20:00+)

`[!] late_track_smoke` — **"Сентрі на pillars/roshpit перед об'єктом"**
- fire_at: при спавні Roshan через 4:30 (тобто 4:00 після last_rosh_kill)
- due_at: spawn + 1:00
- done_when: `sentry_placed` в `roshpit_area`
- rationale: smoke-rosh ворога без сентрі = проїбаний aegis.

`[!] late_dewards_pre_obj` — **"Зачистити ворожі вежі-обзорки перед high ground"**
- trigger: team net worth lead ≥ 8k AND clock > 25:00
- due_at: за 2:00 до push'а (евристика)
- done_when: ≥2 wards_killed в [trigger, trigger+2:00]
- rationale: пуш у hg без вижену = повний фейл.

---

## G. Quality-of-life / micro-quests (можна викинути, але прикольно)

`[·] qol_tp_scroll_carried` — **"Завжди TP в інвентарі після 1:00"**
- check_every: 30s
- alert_when: tp_scroll == 0 continuously for 20s AND clock > 1:00
- rationale: −1 TP = пропущений save товариша.

`[·] qol_buy_observers_charge` — **"Зайди в магаз — є чардж сентрі/обсерв"**
- trigger: ward charge available AND близько до фонтану
- done_when: ward куплений
- rationale: чарджі обсерверів = безкоштовний візіон.

---

## H. Що НАВМИСНО не включаю
- Roshan/Tormentor таймери (Dota показує сама).
- Stack ancients у мід-фазі (умова важка для GSI).
- Heroic items pathing (це не таймінг-квест, це окрема рекомендаційна гілка).
- Шеймс ворожих vision-точок (потребує image-recognition, не GSI).

---

## Notes для рев'ю
- Перевір чи зону `enemy_pull_camp_T1` нормально мапиться на координати в `data/zones.json`.
- `picked_rune`, `picked_lotus`, `wisdom` тощо — поки що hypothetical event'и; треба буде або витягати з GSI або реконструювати з diff'у `items`/`buffs`.
- `nearest_enemy` рахуємо через `MinimapPresenceTracker`.
- Cooldown'и між повторами не вказані — додам defaults у engine.
