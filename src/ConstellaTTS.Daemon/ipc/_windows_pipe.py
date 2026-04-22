"""
Windows named pipe transport, backed by asyncio's IOCP proactor.

Uses `ProactorEventLoop.start_serving_pipe(protocol_factory, address)`
for true overlapped I/O — reads and writes are queued to the kernel and
completed asynchronously, so concurrent R/W on the same pipe handle
doesn't deadlock the way synchronous-mode pipes do.

## Architecture
1. `WindowsPipeTransport.listen(name, handler)` calls
   `loop.start_serving_pipe(factory, pipe_path)`. The proactor internally
   keeps creating fresh pipe instances and accepting clients on them;
   each accepted connection instantiates a new `_PipeProtocol`.
2. `_PipeProtocol` bridges the raw proactor byte stream into an
   `asyncio.StreamReader` / `StreamWriter` pair — the same abstraction
   used by `asyncio.start_server` over TCP.
3. The user-supplied handler coroutine runs as a task with those streams.

Only imported on Windows.
"""

from __future__ import annotations

import asyncio
import logging
import os
import sys

if sys.platform != "win32":
    raise ImportError("_windows_pipe is Windows-only")

from ipc.transport import ConnectionHandler, Endpoint, Transport


_log = logging.getLogger(__name__)


#: Max concurrent connections across the whole transport. Not a security
#: measure — internal safety net against connection-leak bugs that might
#: otherwise exhaust kernel handle limits.
POOL_LIMIT = 50


def _pipe_name(daemon_id: str, channel: str) -> str:
    r"""
    Build a Windows named pipe path.

    ## Parameters
    - `daemon_id` — Per-instance identifier. We use the daemon's PID so
      the C# host can deterministically derive the control pipe path from
      `Process.Id` without the daemon announcing it.
    - `channel` — Logical channel name (e.g. `"control"`, `"job_abc"`).

    ## Returns
    Full pipe path like `\\.\pipe\constella_<daemon_id>_<channel>`.
    The `\\.\pipe\` prefix is the standard local named pipe namespace.
    """
    return rf"\\.\pipe\constella_{daemon_id}_{channel}"

class WindowsPipeTransport(Transport):
    """
    Windows named pipe transport.

    One call to `listen(name, handler)` → one pipe path being served; the
    proactor keeps accepting new clients on that path for as long as the
    server is up. Each accepted connection becomes an independent
    StreamReader/StreamWriter pair passed to a fresh handler task.
    """

    def __init__(self) -> None:
        # Use the PID as the daemon id. The C# host knows the PID from
        # `Process.Start` and can derive the control pipe path directly
        # without any stdout announcement — which has turned out to be
        # unreliable on Windows (buffered text readers stealing bytes
        # from the BaseStream, etc.).
        self._daemon_id = str(os.getpid())

        # Track live handler tasks so close() can cancel them.
        self._handler_tasks: set[asyncio.Task] = set()

        # asyncio-level "server" objects returned by start_serving_pipe.
        # We keep references so we can close them during shutdown.
        self._servers: list[tuple[str, list]] = []

        self._closing = False

    # ── Transport interface ──

    async def listen(self, name: str, handler: ConnectionHandler) -> Endpoint:
        if self._closing:
            raise RuntimeError("transport is closing")

        loop = asyncio.get_running_loop()
        # start_serving_pipe exists on ProactorEventLoop. We defensively
        # check because a wrong loop policy would give a confusing
        # AttributeError otherwise.
        if not hasattr(loop, "start_serving_pipe"):
            raise RuntimeError(
                "Windows pipe transport requires a ProactorEventLoop "
                f"(got {type(loop).__name__}). asyncio.run() uses this by "
                "default on Windows; don't override the event loop policy."
            )

        pipe_path = _pipe_name(self._daemon_id, name)

        def protocol_factory() -> asyncio.Protocol:
            return _PipeProtocol(self, handler)

        # Returns a list of `PipeServer` objects (one per path). The list
        # is a CPython implementation detail; we just keep it alive so
        # the proactor doesn't garbage-collect the accept loop.
        server_list = await loop.start_serving_pipe(
            protocol_factory, pipe_path
        )
        self._servers.append((pipe_path, server_list))

        _log.info("Listening on pipe:%s", pipe_path)
        return Endpoint(kind="pipe", address=pipe_path)

    async def close(self) -> None:
        self._closing = True

        # Stop accepting new connections.
        for _path, server_list in self._servers:
            for server in server_list:
                try:
                    server.close()
                except Exception:
                    _log.exception("error closing pipe server")
        self._servers.clear()

        # Cancel active handlers.
        tasks = list(self._handler_tasks)
        for t in tasks:
            t.cancel()
        for t in tasks:
            try:
                await t
            except (asyncio.CancelledError, Exception):
                pass
        self._handler_tasks.clear()

    # ── Per-connection bookkeeping ──

    def _register_handler(self, task: asyncio.Task) -> bool:
        """
        Track a freshly-spawned handler task. Returns False if the pool
        is full (caller should abort the connection).
        """
        if len(self._handler_tasks) >= POOL_LIMIT:
            return False
        self._handler_tasks.add(task)
        task.add_done_callback(self._handler_tasks.discard)
        return True


# ── Protocol: one per accepted pipe connection ──────────────────────────────


class _PipeProtocol(asyncio.Protocol):
    """
    Bridges raw proactor bytes into a StreamReader/StreamWriter pair.

    Each accepted pipe connection gets its own `_PipeProtocol` instance.
    We construct a `StreamReader` and feed it via `data_received`,
    construct a matching `StreamWriter` over the transport, then invoke
    the user's handler coroutine.
    """

    def __init__(
        self,
        owner: WindowsPipeTransport,
        handler: ConnectionHandler,
    ) -> None:
        self._owner = owner
        self._handler = handler
        self._transport: asyncio.BaseTransport | None = None

        loop = asyncio.get_event_loop()
        self._reader = asyncio.StreamReader(loop=loop)
        self._stream_protocol = asyncio.StreamReaderProtocol(
            self._reader, loop=loop
        )
        self._handler_task: asyncio.Task | None = None

    def connection_made(self, transport: asyncio.BaseTransport) -> None:
        self._transport = transport
        # Let the stream protocol know about the transport so its writer
        # drain logic works correctly.
        self._stream_protocol.connection_made(transport)

        writer = asyncio.StreamWriter(
            transport, self._stream_protocol, self._reader,
            asyncio.get_event_loop(),
        )

        task = asyncio.create_task(
            self._run_handler(writer),
            name="pipe-handler",
        )
        if not self._owner._register_handler(task):
            _log.warning("connection pool full, dropping new connection")
            task.cancel()
            transport.close()
            return

        self._handler_task = task

    def data_received(self, data: bytes) -> None:
        self._stream_protocol.data_received(data)

    def eof_received(self) -> bool | None:
        return self._stream_protocol.eof_received()

    def connection_lost(self, exc: BaseException | None) -> None:
        self._stream_protocol.connection_lost(exc)

    async def _run_handler(self, writer: asyncio.StreamWriter) -> None:
        try:
            await self._handler(self._reader, writer)
        except asyncio.CancelledError:
            raise
        except Exception:
            _log.exception("handler raised")
        finally:
            try:
                writer.close()
                await writer.wait_closed()
            except Exception:
                pass
