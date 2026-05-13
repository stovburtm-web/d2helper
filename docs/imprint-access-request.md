D2Helper — Imprint.gg API access request

Project summary

D2Helper is a Windows desktop companion app for Dota 2 players. It runs locally next to the game and combines Valve's Game State Integration feed with public match-data APIs (OpenDota, Stratz) to assist the user during a session. The exact feature set is still being shaped, but the general direction is active, in-match help rather than after-the-fact statistics — things like pre-game opponent intel during the draft, item and pick suggestions tied to the actual enemy lineup in the current patch, and timing reminders for objectives like Roshan, Tormentor and rune spawns.

The app is built on .NET 8 with an Avalonia UI and a Direct2D in-game overlay. All in-game data comes exclusively from Valve's GSI — no memory reading, no DLL injection, no anti-cheat surface. We are interested in Imprint.gg because, alongside post-match providers, we want a forward-looking signal about the lobby and the queue context (queue type, MMR distribution, stack detection, region/language fit). Expected volume during the MVP closed beta is low — single-digit requests per matched game, a few hundred users, hard-capped client-side, results cached in local SQLite. We will respect any rate-limit / quota, will not redistribute raw responses, and are happy to attribute Imprint.gg as a data source in the UI and credits.

Contact: fill in your name + email + Discord
Stage: pre-public MVP, week 1 of development
Repo: https://github.com/stovburtm-web/d2helper (private during MVP, access on request)
