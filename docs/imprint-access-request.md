D2Helper — Imprint.gg API access request

Project summary

D2Helper is a Windows desktop companion app for Dota 2 players. It runs locally next to the game and combines Valve's Game State Integration feed with public match-data APIs (OpenDota, Stratz) to assist the user during a session. The exact feature set is still being shaped, but the general direction is active, in-match help rather than after-the-fact statistics — things like pre-game opponent intel during the draft, item and pick suggestions tied to the actual enemy lineup in the current patch, and timing reminders for objectives like Roshan, Tormentor and rune spawns.

The app is built on .NET 8 with an Avalonia UI and a Direct2D in-game overlay. All in-game data comes exclusively from Valve's GSI — no memory reading, no DLL injection, no anti-cheat surface. We are interested in Imprint.gg because, alongside post-match providers, we want a forward-looking signal about the lobby and the queue context (queue type, MMR distribution, stack detection, region/language fit). Expected volume during the MVP closed beta is low — single-digit requests per matched game, a few hundred users, hard-capped client-side, results cached in local SQLite. We will respect any rate-limit / quota, will not redistribute raw responses, and are happy to attribute Imprint.gg as a data source in the UI and credits.

Contact: fill in your name + email + Discord
Stage: pre-public MVP, week 1 of development
Repo: https://github.com/stovburtm-web/d2helper (private during MVP, access on request)

---

## Reply to Adam (2026-05-14) — data we'd like from Imprint

Adam asked for a more concrete list. Below is what maps onto our roadmap.

### High value — would integrate during MVP
- **GetLeagueHeroStatistics** — pro pick/ban + win-rate per hero on the live patch. We want this as the trusted baseline for the draft helper (Stage 4 in `docs/roadmap.md`). Pub-tier data from OpenDota is noisy; pro data is the cleanest "what's actually meta right now" signal.
- **GetLeaguePlayerHeroStatistics** — builds, skill order and item timings of top players on a given hero. Feeds the in-overlay item recommendation panel (Stage 5). The pitch to the user is literally "here's how the best Invoker in the world builds this matchup on the current patch".
- **GetMatchData** (and **GetSeriesData** as the index) — granular per-match timeline. Critical question: does the payload include event timestamps (rune pickups, lotus picks, stack camps, ward placements, smoke usage, rotations)? If yes, this becomes the source of truth for building our role playbooks (Stage 6) — i.e. "when does a pro pos-5 actually rotate to mid", "when does a pro mid hit the 2:00 rune". That directly powers the timing-quest core of the product.
- **GetLeagueTeamHeroStatistics** — team-comp synergy data. Used as the synergy term in the draft scoring function.

### Medium value — post-MVP
- **Webhooks: Subscribe** — push notifications on league match start. We'd use this for an optional "Watch & learn — pro game just started on the hero you main" prompt in the companion window.
- **GetSeasonHeroRankings** — meta drift over a season for retrospective UI.

### Not needed for our use case
- `QueueMatch`, `QueueLeague` — tournament orchestration, outside our scope.
- `GetLeagueFixtures`, `GetLeaguePlayers`, `GetLeagueTeams`, `GetLeagueMatches` — schedule / roster / index endpoints. We'd only hit `GetLeagueMatches` as a discovery step before `GetMatchData`.
- `GetSeasonLeagues`, `GetSeasonTeams`, `GetSeasonPlayerRankings`, `GetSeasonTeamRankings` — leaderboard UX, not relevant to a per-pub-match coach.

### Open questions for Imprint
1. Is there any **pub / ranked match** data exposed, or is the dataset strictly tournament / league play? If pro-only, we'll position it as a meta baseline rather than a primary source.
2. Can `GetLeagueHeroStatistics` and `GetLeaguePlayerHeroStatistics` be **filtered by patch** (e.g. `?patch=7.39c`)? Patch granularity is required, because pre/post patch numbers are not comparable.
3. **Replay-level granularity** in `GetMatchData` — does the payload include per-event timestamps (runes, lotus, stacks, wards, smokes, rotations)? That's the make-or-break field for the playbook builder.
4. **Rate limits and pricing** for an indie pre-revenue MVP (low single-digit requests per user per matched game, a few hundred users, results cached client-side in SQLite).
5. **ToS** — is it acceptable to display aggregated Imprint-sourced numbers inside an overlay rendered on top of a live Dota client (with Imprint attribution shown)?
6. Format: REST JSON only, or is there a GraphQL / streaming option?
