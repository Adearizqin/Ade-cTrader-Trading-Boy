# Demo Validation Checklist

**Live trading is prohibited.** The cBot now contains an experimental EMA-crossover demo entry engine, but it has not been compiled or validated in cTrader. Entry remains disabled by default.

## 1. Build in cTrader Automate

- [ ] Open the cTrader application that supports Automate/cBots.
- [ ] Create or open a cBot project and import `src/AdeRiskBot.cs`.
- [ ] Compile in the target cTrader Automate version.
- [ ] Save the full compiler output with the test record.
- [ ] Do not continue to account testing if there are compiler errors or unresolved API warnings.

GitHub Actions checks the Python controller and static safety contracts. It does **not** compile C# against the cTrader Automate API.

## 2. Start only on a demo account

Use a dedicated demo account and the broker's exact gold symbol (for example, `XAUUSD`, if that is the symbol name shown by the broker).

Recommended initial parameters:

- Enable Entries: `false`
- Risk Per Trade: `1.0%`
- Daily Loss Limit: `3.0%`
- Trailing Stop Enabled: `true`

Confirm that the startup log identifies demo-only mode and that no entry order is submitted while Enable Entries is false.

## 2A. Validate the experimental entry engine (demo only)

Do this only after the cBot compiles without errors and the cutoff/trailing checks above have passed.

- Strategy: EMA 9/21 crossover on closed bars; buy when fast EMA crosses above slow EMA and sell when it crosses below.
- Entry timeframe: the chart timeframe where the cBot is attached. No multi-timeframe confirmation is implemented.
- Symbol: keep Require Gold Symbol enabled; the current symbol name must contain `XAU` or `GOLD`.
- Exposure: at most one position owned by this bot for the current symbol.
- Protection: order submission includes the configured stop-loss and take-profit in pips; volume is calculated from the 1% equity risk target and configured stop distance.
- Start with a cTrader backtest on the broker's exact gold symbol and account type. Include spread, commission, and realistic slippage where the platform supports them.
- Review trade count, drawdown, expectancy, average win/loss, and behavior in trending and ranging periods. A crossover signal is not evidence of profitability.
- Only after reviewing the backtest, run a short demo forward test with Enable Entries set to true. Keep the daily cutoff at 3%, verify order logs and attached SL/TP, and stop the test if any safety behavior differs from expectation.
- The default stop-loss/take-profit values are placeholders, not broker-validated XAUUSD settings. Confirm pip size, minimum stop distance, volume minimum/step, and contract specification first.
- If any order is rejected, inspect the cTrader error log. Do not increase risk or remove protective stops to force an order through.

## 3. Verify the daily cutoff

Use a demo environment and a controlled test plan. Do not intentionally risk meaningful funds to force a loss.

- [ ] Record the account equity and broker-server date at startup.
- [ ] Confirm the baseline remains stable after a cBot restart on the same server day.
- [ ] Confirm a new baseline is recorded after the broker-server date changes.
- [ ] Verify that reaching the configured daily equity-loss threshold blocks further entries.
- [ ] Verify that only positions carrying the bot label and current symbol are targeted for closure.
- [ ] Verify that successful closure is logged only after cTrader confirms it.
- [ ] Simulate or observe a failed close safely and confirm that retries continue while the cutoff is active.
- [ ] Check the logs for any positions that remain open after a close failure; the bot cannot guarantee closure during a disconnection or broker rejection.

## 4. Verify trailing-stop behavior

- [ ] Test a bot-labelled demo position in each direction.
- [ ] Confirm the stop only tightens and never moves farther away from the market.
- [ ] Confirm modification failures are visible in the log.
- [ ] Confirm trailing management stops after the daily cutoff.

## 5. Termux connectivity status

The current Termux controller listens only on `127.0.0.1:8765` and exposes a read-only `GET /health` endpoint. Its `POST` endpoints reject commands. It does not connect to cTrader, and a loopback address on an Android phone is not automatically reachable from a separate desktop or cloud cTrader runtime.

Do not expose the controller publicly or add broker credentials to the repository. A separate authenticated bridge design is required before Termux can exchange status or commands with a cBot. Keep order-control disabled until that design is reviewed and tested.

## Test evidence record

Record the date, cTrader version, broker server, demo symbol, compiler output, test results, and any failed-close/trailing-stop logs. Never include passwords, API tokens, or account credentials.
