# Ade cTrader Trading Bot

**Status: scaffold / not yet validated in cTrader Automate. Demo-only development.**

This repository will contain a cTrader Automate cBot and a small Termux-side controller. It is separate from Spotware's sample repository, which is used only as a reference.

## Non-negotiable safety rules

- The cBot must refuse to start on a live account.
- Default state is **entries disabled** until demo compilation and test evidence are reviewed.
- Target risk per trade: 1% of equity, calculated from the actual stop-loss distance and the broker's symbol volume rules.
- Daily loss cutoff: 3% of the equity baseline captured at the start of the broker-server day. When reached, block new entries and attempt to close positions owned by this bot. A failed close must be logged and retried; do not report a close as successful unless cTrader confirms it.
- Trailing stop must only tighten protection, never widen the stop-loss.
- Never store broker passwords, API secrets, or account tokens in this repository.
- Termux is a control/monitoring client, not a replacement for the cTrader Automate runtime.

## Planned layout

- `src/AdeRiskBot.cs` — cTrader Automate cBot; demo guard, risk controls, experimental EMA 9/21 crossover entry (disabled by default).
- `bridge/termux_controller.py` — local controller/health-check scaffold; no public listener by default.
- `config.example.json` — non-secret example configuration.
- `tests/` — tests for the controller and risk calculations.

## Build and test workflow

1. Open this repository in GitHub Codespaces to edit and review source.
2. Compile the cBot in a compatible cTrader desktop/Automate environment; Codespaces alone is not a cTrader execution runtime.
3. Run backtests and then demo tests only. The experimental entry is an EMA 9/21 crossover on the attached chart timeframe, not a proven strategy. Verify symbol contract specifications, volume rounding, stop-distance rules, daily reset behavior, reconnect behavior, and failed-close handling before enabling entries.
4. Keep `Enable Entries` off until the review is complete.

## Current limitations

This is not a verified or production-ready trading system. The EMA crossover is experimental and has no verified profitability. No live trading is supported or authorized. The Termux controller does not place orders directly; execution remains inside the cTrader cBot.
