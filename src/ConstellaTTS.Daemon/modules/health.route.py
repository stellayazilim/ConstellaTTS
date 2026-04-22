"""
Health / introspection route.

Reports daemon liveness and the set of currently registered routes.
This is the first thing a C# client can call after connecting to
verify the daemon is responsive and to discover what's available.

## Actions
- `check(data)` → simple liveness probe.
- `routes(data)` → list every registered route and its actions.
- `info(data)` → daemon process info (pid, python version, uptime).
"""

from __future__ import annotations

import os
import platform
import sys
import time


#: Recorded once at import time so `info` can report uptime.
_STARTED_AT = time.time()


class HealthRoute:
    """Built-in introspection endpoint."""

    name = "health"

    async def check(self, data: dict) -> dict:
        """
        Liveness probe.

        ## Returns
        `{"ok": True, "timestamp": <unix seconds>}`.
        """
        return {"ok": True, "timestamp": time.time()}

    async def routes(self, data: dict) -> dict:
        """
        List every registered route and its actions.

        ## Returns
        `{"routes": {route_name: [action, ...], ...}}`.
        """
        # Imported lazily to avoid a circular import: registry imports this
        # module during discovery, and this action calls back into registry.
        from router import registry
        return {"routes": registry.list_routes()}

    async def info(self, data: dict) -> dict:
        """
        Daemon process metadata.

        ## Returns
        Dict with `pid`, `python_version`, `platform`, `uptime_seconds`.
        """
        return {
            "pid": os.getpid(),
            "python_version": sys.version.split()[0],
            "platform": platform.platform(),
            "uptime_seconds": time.time() - _STARTED_AT,
        }


Route = HealthRoute()
