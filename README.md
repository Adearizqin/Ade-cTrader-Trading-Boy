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

## Build the .algo file from Android with GitHub Actions

The workflow `.github/workflows/build-ctrader-algo.yml` uses the official Spotware cTrader CLI Docker image on a GitHub-hosted Linux runner. The build and metadata commands do not need cTrader account credentials, and the workflow does not place orders.

1. Push updates to `src/AdeRiskBot.cs` or the build workflow on branch `Blogger`. You can also run **Build cTrader cBot (.algo)** manually from the repository's Actions tab.
2. Open the successful workflow run and download the artifact named `AdeRiskBot-algo-demo-candidate`.
3. Extract the ZIP to get `AdeRiskBot.algo`.
4. On Android, tap the `.algo` file and select cTrader, or use the Upload option in the cTrader Algo/cBots interface. See the [official cTrader installation instructions](https://help.ctrader.com/cbots/how-tos/install-a-cbot/).

[Latest successful build run](https://github.com/Adearizqin/Ade-cTrader-Trading-Boy/actions/runs/40339243638)

The 26 October 2026 build reported `success: true`, no compiler errors, and valid cBot metadata. It did emit `CS1701` assembly-version warnings concerning `System.Runtime` 6.0 versus 10.0. Treat this as a successful build with warnings, not proof that the bot has been validated inside a live cTrader runtime.

## Demo validation

- Keep `Enable Entries = false` for the first installation.
- Use only a dedicated demo account and the broker's exact gold symbol (for example, `XAUUSD` if that is the symbol name shown by the broker).
- Confirm that cTrader starts the bot and inspect its logs. Verify the symbol contract specification, SL/TP distances, volume, daily cutoff, restart behavior, and trailing stop.
- Only after successful runtime checks should you consider a controlled demo forward test with entries enabled. The EMA 9/21 crossover is experimental and not evidence of profitability.
- GitHub Actions also runs Python controller tests and static C# safety-contract checks; those tests do not simulate a broker or prove order execution.
  
## Current limitations

This is not a verified or production-ready trading system. The EMA crossover is experimental and has no verified profitability. No live trading is supported or authorized. The Termux controller does not place orders directly; execution remains inside the cTrader cBot.
