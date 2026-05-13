# D2Helper

Десктопний помічник для Dota 2 (Windows) з real-time підказками під час матчу:
counter-pick у драфті, рекомендації шмоток/розкачки проти конкретних ворожих героїв у поточному патчі, тайминг-алерти (Roshan, Tormentor), аналіз опонентів.

> Це **не** ще одна статистика a-la Overwolf Dota Plus. Це **активний помічник**.

## Документація
- [docs/dota2-gsi.md](docs/dota2-gsi.md) — як отримуємо дані з гри (GSI).
- [docs/overwolf.md](docs/overwolf.md) — огляд Overwolf і чому ми його не беремо.
- [docs/external-apis.md](docs/external-apis.md) — OpenDota, Stratz, Steam Web API.
- [docs/architecture.md](docs/architecture.md) — модулі, вибір стеку, схема.
- [.github/copilot-instructions.md](.github/copilot-instructions.md) — інструкції для AI-агента.

## Стек (рекомендований)
**.NET 8 / C# 12 / Avalonia / Dota2GSI / GameOverlay.Net / SQLite / Rx.NET**

Чому C#: див. [docs/architecture.md](docs/architecture.md).

## Статус
🟡 Pre-init. Solution ще не створено. Зробити після затвердження стеку.
