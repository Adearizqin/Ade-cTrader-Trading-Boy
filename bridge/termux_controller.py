"""Local-only control/status scaffold for Ade cTrader Trading Bot.

This module does not connect to a broker and does not place or modify orders.
It deliberately binds to loopback only. Do not expose this server publicly.
"""
from __future__ import annotations

import argparse
import json
from http.server import BaseHTTPRequestHandler, HTTPServer
from typing import Any


HOST = "127.0.0.1"
PORT = 8765


class Handler(BaseHTTPRequestHandler):
    def _reply(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/health":
            self._reply(200, {
                "status": "ok",
                "component": "termux-controller-scaffold",
                "broker_connected": False,
                "orders_enabled": False,
                "mode": "demo-only",
            })
            return
        self._reply(404, {"error": "not_found"})

    def do_POST(self) -> None:  # noqa: N802
        # No order-control endpoints are intentionally implemented yet.
        self._reply(403, {
            "error": "control_not_enabled",
            "message": "This scaffold cannot send trading commands.",
        })

    def log_message(self, fmt: str, *args: object) -> None:
        print("controller:", fmt % args)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default=HOST, choices=[HOST],
                        help="Loopback only; remote bind is prohibited.")
    parser.add_argument("--port", type=int, default=PORT)
    args = parser.parse_args()
    server = HTTPServer((args.host, args.port), Handler)
    print(f"Health endpoint: http://{args.host}:{args.port}/health")
    print("Demo-only scaffold; no broker connection or order endpoints.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopping controller.")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
