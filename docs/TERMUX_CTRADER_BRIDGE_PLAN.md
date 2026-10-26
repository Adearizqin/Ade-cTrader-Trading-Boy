# Termux–cTrader Bridge Plan

**Status: design only. No network bridge or order-control channel is implemented.**

## Current verified boundary

- The cBot executes inside the cTrader Automate runtime.
- The Android Termux controller binds to `127.0.0.1:8765`, serves read-only `GET /health`, and rejects every `POST` request.
- Loopback on Android is reachable only from the same network namespace/device. It is not automatically reachable from a cTrader instance running on another computer or cloud runtime.
- GitHub Actions validates Python tests and static safety contracts. It does not compile or run the cBot.

## Recommended staged design

### Stage 0 — local scaffold (current)

Keep all order-control endpoints disabled. Confirm the cBot compiles and passes demo safety tests before designing remote control.

### Stage 1 — read-only status relay

If Termux and cTrader run on different devices, use a private authenticated HTTPS relay rather than opening a public port on the phone.

The cBot would publish a small allowlisted status document, such as:

- mode (must be demo)
- broker-server date
- day-start equity and current equity
- cutoff state
- bot version and last heartbeat
- bot-labelled position count

Do not publish account number, personal data, access tokens, or full broker logs. Do not include trade commands in this stage.

### Stage 2 — constrained demo commands (not approved or implemented)

Only consider commands after Stage 1 is reviewed. Requirements:

- Explicit demo-account verification on the cTrader side; fail closed if account type cannot be verified.
- TLS and strong authentication; secrets stored outside source control.
- Allowlisted commands only; no arbitrary code, shell commands, symbol changes, or generic order payloads.
- Timestamp expiry, unique command ID, replay protection, and idempotency.
- Hard server-side risk caps: maximum 1% equity risk per trade and a 3% daily equity-loss cutoff based on the broker-server day's starting equity.
- Cutoff state must override every entry command.
- Auditable acknowledgements distinguish received, rejected, submitted, and broker-confirmed outcomes.
- Timeouts, disconnects, malformed payloads, or unknown state must fail closed.
- Test on demo with entries disabled first. Do not enable entry commands until an independent review and documented test evidence exist.

## Why not expose the current controller?

The current controller has no authentication because it is deliberately loopback-only and read-only. Changing its bind address or opening a public port would increase exposure without creating a valid cTrader connection. Do not expose it, add order endpoints, or put API keys in this repository.

## Next practical steps

1. Import `src/AdeRiskBot.cs` into a compatible cTrader Automate environment and compile it.
2. Resolve all compiler errors against that exact cTrader API version.
3. Complete the checks in `docs/DEMO_VALIDATION_CHECKLIST.md`.
4. Share the compiler output and demo test evidence (with account identifiers and credentials redacted).
5. Review a read-only status relay before considering any remote command channel.

## Acceptance criteria

The bridge is not considered connected until an actual cTrader runtime and Termux instance exchange an authenticated, timestamped status message and logs prove receipt on both sides. A successful HTTP health check or passing Python tests alone is not evidence of a cTrader connection.
