# Demo Validation Checklist

**Do not enable live trading.** The current cBot is a safety scaffold and contains no entry signal engine.

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

Confirm that the startup log identifies demo-only mode and that no entry order is submitted.

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
