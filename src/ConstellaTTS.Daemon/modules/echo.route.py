"""
Echo route — development-time sanity check.

Sends back whatever payload the client sent, with a timestamp added.
Used to verify that the control pipe, framing, router, and dispatch paths
all work end-to-end before any real model is involved.

## Actions
- `say(data)` → echoes `data` back with `received_at` timestamp added.
- `ping(data)` → returns `{"pong": True}` without touching the payload.
"""

from __future__ import annotations

import time


class EchoRoute:
    """A route that does nothing useful — exists only for testing."""

    name = "echo"

    async def say(self, data: dict) -> dict:
        """
        Echo the payload back with a receive timestamp.

        ## Parameters
        - `data` — Arbitrary dict; returned verbatim with a timestamp added.

        ## Returns
        The input dict with a `received_at` (Unix seconds) field merged in.
        """
        return {**data, "received_at": time.time()}

    async def ping(self, data: dict) -> dict:
        """
        Trivial liveness check.

        ## Returns
        `{"pong": True}`.
        """
        return {"pong": True}


#: Module-level export — the registry picks this up by convention.
Route = EchoRoute()
