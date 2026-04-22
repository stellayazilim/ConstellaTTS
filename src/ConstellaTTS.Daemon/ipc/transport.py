"""
Transport abstraction — platform-agnostic IPC.

Each platform provides a concrete `Transport` that can listen on a named
endpoint and accept incoming connections. Every accepted connection is
handed to a user-provided handler as a `(reader, writer)` pair with an
asyncio-compatible interface.

- **Unix**: Unix domain socket, native asyncio.
- **Windows**: named pipe, ctypes + thread bridge.

Both paths ultimately expose the same connection shape, so higher layers
(control session, job socket, router) don't need to know which OS they're on.
"""

from __future__ import annotations

import sys
from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Awaitable, Callable, Protocol


# ── Endpoint description ──────────────────────────────────────────────────────


@dataclass(frozen=True)
class Endpoint:
    """
    Identifies an IPC endpoint, communicated to the C# client so it knows
    where to connect.

    ## Fields
    - `kind` — `"unix"` for Unix domain socket, `"pipe"` for Windows named pipe.
    - `address` — Filesystem path (unix) or pipe name like
      `\\\\.\\pipe\\constella_xxx` (pipe).
    """

    kind: str
    address: str

    def to_dict(self) -> dict:
        """Serialize to a plain dict for MessagePack transport."""
        return {"kind": self.kind, "address": self.address}


# ── Connection protocol — what handlers receive ──────────────────────────────


class Reader(Protocol):
    """
    Async byte reader. `asyncio.StreamReader` satisfies this automatically;
    the Windows pipe wrapper implements it manually.
    """

    async def readexactly(self, n: int) -> bytes: ...


class Writer(Protocol):
    """
    Async byte writer. `asyncio.StreamWriter` satisfies this automatically;
    the Windows pipe wrapper implements it manually.
    """

    def write(self, data: bytes) -> None: ...
    async def drain(self) -> None: ...
    def close(self) -> None: ...
    async def wait_closed(self) -> None: ...


#: Type alias for a connection handler — invoked once per accepted connection.
ConnectionHandler = Callable[[Reader, Writer], Awaitable[None]]


# ── Transport abstract base ──────────────────────────────────────────────────


class Transport(ABC):
    """
    Platform-specific IPC server. Subclasses handle the actual listen/accept
    mechanics. Use `Transport.create()` to get the right one for the current
    platform.
    """

    @abstractmethod
    async def listen(self, name: str, handler: ConnectionHandler) -> Endpoint:
        """
        Start listening on an endpoint derived from `name`.

        ## Parameters
        - `name` — Logical channel name, e.g. `"control"` or `"job_abc"`.
        - `handler` — Invoked once per accepted connection, in its own task.

        ## Returns
        The `Endpoint` (so it can be communicated to the C# client).
        """
        ...

    @abstractmethod
    async def close(self) -> None:
        """Stop accepting new connections and close all active ones."""
        ...

    @staticmethod
    def create() -> "Transport":
        """
        Factory — returns the platform-appropriate transport implementation.

        ## Returns
        `WindowsPipeTransport` on Windows, `UnixSocketTransport` elsewhere.
        """
        if sys.platform == "win32":
            from ipc._windows_pipe import WindowsPipeTransport
            return WindowsPipeTransport()
        else:
            from ipc._unix_socket import UnixSocketTransport
            return UnixSocketTransport()
