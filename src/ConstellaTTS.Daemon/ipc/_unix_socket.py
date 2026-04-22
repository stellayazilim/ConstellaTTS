"""
Unix domain socket transport.

Straightforward asyncio implementation: each call to `listen()` starts a
unix server at a user-scoped socket path, and every accepted connection
spawns a task running the provided handler.

On Unix, asyncio's `StreamReader`/`StreamWriter` directly satisfy the
`Reader`/`Writer` protocols declared in `transport.py` — no wrapping needed.
"""

from __future__ import annotations

import asyncio
import logging
import os
import tempfile

from ipc.transport import ConnectionHandler, Endpoint, Transport


_log = logging.getLogger(__name__)


class UnixSocketTransport(Transport):
    """Unix domain socket transport — asyncio native, lock-free per pipe."""

    def __init__(self) -> None:
        # Tracked so close() can clean up.
        self._servers: list[asyncio.base_events.Server] = []
        self._paths: list[str] = []

    async def listen(self, name: str, handler: ConnectionHandler) -> Endpoint:
        """
        Start a Unix domain socket server at a path derived from `name` and
        the daemon PID.

        ## Parameters
        - `name` — Logical channel name (e.g. `"control"`, `"job_abc"`).
        - `handler` — Coroutine called once per accepted connection.

        ## Returns
        The `Endpoint` describing the socket's filesystem path.

        ## Notes
        - Stale socket files from crashed previous runs are removed before
          binding.
        - The socket is chmod'd to `0o600` so only the current user can
          connect to it.
        """
        path = self._socket_path(name)

        # Remove any stale socket file left over from a crashed previous run.
        if os.path.exists(path):
            try:
                os.unlink(path)
            except OSError as e:
                _log.warning("Could not remove stale socket %s: %s", path, e)

        server = await asyncio.start_unix_server(handler, path=path)

        # Restrict access to current user only. asyncio.start_unix_server
        # creates the socket with the default umask, so we tighten it here.
        try:
            os.chmod(path, 0o600)
        except OSError as e:
            _log.warning("Could not chmod socket %s: %s", path, e)

        self._servers.append(server)
        self._paths.append(path)

        _log.info("Listening on unix:%s", path)
        return Endpoint(kind="unix", address=path)

    async def close(self) -> None:
        """Stop all servers and remove their socket files."""
        for server in self._servers:
            server.close()
            await server.wait_closed()
        self._servers.clear()

        for path in self._paths:
            try:
                if os.path.exists(path):
                    os.unlink(path)
            except OSError as e:
                _log.warning("Could not remove socket %s on close: %s", path, e)
        self._paths.clear()

    @staticmethod
    def _socket_path(name: str) -> str:
        """
        Build a unique socket path for this daemon instance.

        ## Notes
        PID is embedded in the filename so multiple daemons on the same
        machine (e.g. dev + test) don't collide.
        """
        filename = f"constella_{name}_{os.getpid()}.sock"
        return os.path.join(tempfile.gettempdir(), filename)
