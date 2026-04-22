"""
Length-prefixed MessagePack framing.

Wire format:
    [4 bytes: little-endian uint32 = payload length][MessagePack payload]

Provides both async and sync read/write helpers. Async helpers expect an
`asyncio.StreamReader` / `asyncio.StreamWriter` duck-type (the Unix domain
socket path). Sync helpers expect any object with `.read(n)` and `.write(data)`
methods (the Windows named pipe wrapper path, thread-blocking).

Max frame size is bounded to prevent a malicious or buggy peer from triggering
OOM by sending a huge length header.
"""

from __future__ import annotations

import struct
from typing import Protocol

import msgpack


# ── Configuration ─────────────────────────────────────────────────────────────

#: Upper bound on a single frame payload. Audio chunks (~3 MB for 40s @ 24kHz
#: WAV) fit comfortably; anything larger is almost certainly a bug or misuse.
MAX_FRAME_SIZE = 16 * 1024 * 1024  # 16 MiB

#: Length prefix format. `<I` = little-endian unsigned 32-bit.
#: Kept little-endian for continuity with the original stdin/stdout prototype.
_HEADER_FMT = "<I"
_HEADER_SIZE = 4


# ── Exceptions ────────────────────────────────────────────────────────────────


class FramingError(Exception):
    """Base class for all framing protocol errors."""


class FrameTooLargeError(FramingError):
    """Peer sent a length header exceeding `MAX_FRAME_SIZE`."""


class InvalidFrameError(FramingError):
    """Payload could not be deserialized as MessagePack."""


class PeerClosedError(FramingError):
    """Peer closed the connection in the middle of (or instead of) a frame."""


# ── Duck-type protocols for readers/writers ──────────────────────────────────


class _SyncReader(Protocol):
    def read(self, n: int) -> bytes: ...


class _SyncWriter(Protocol):
    def write(self, data: bytes) -> int | None: ...


class _AsyncReader(Protocol):
    async def readexactly(self, n: int) -> bytes: ...


class _AsyncWriter(Protocol):
    def write(self, data: bytes) -> None: ...
    async def drain(self) -> None: ...


# ── Sync helpers (used by Windows pipe reader/writer threads) ────────────────


def read_frame_sync(reader: _SyncReader) -> dict | None:
    """
    Read one frame from a blocking byte source.

    Loops over partial reads until the full frame is assembled. Treats a
    clean EOF at a frame boundary as end-of-stream, not as an error.

    ## Parameters
    - `reader` — Any object with a blocking `.read(n) -> bytes` method.

    ## Returns
    The deserialized message dict, or `None` if the peer closed cleanly at
    a frame boundary.

    ## Raises
    - `PeerClosedError` — Peer closed mid-frame (truncated read).
    - `FrameTooLargeError` — Length header exceeds `MAX_FRAME_SIZE`.
    - `InvalidFrameError` — Payload is not valid MessagePack.
    """
    header = _read_exact_sync(reader, _HEADER_SIZE, allow_clean_eof=True)
    if header is None:
        return None  # clean EOF at frame boundary

    (length,) = struct.unpack(_HEADER_FMT, header)
    if length > MAX_FRAME_SIZE:
        raise FrameTooLargeError(
            f"frame length {length} exceeds max {MAX_FRAME_SIZE}"
        )

    payload = _read_exact_sync(reader, length, allow_clean_eof=False)
    assert payload is not None  # allow_clean_eof=False guarantees this

    try:
        return msgpack.unpackb(payload, raw=False)
    except (msgpack.UnpackException, ValueError) as e:
        raise InvalidFrameError(f"msgpack decode failed: {e}") from e


def write_frame_sync(writer: _SyncWriter, msg: dict) -> None:
    """
    Write one frame to a blocking byte sink.

    The header and payload are concatenated into a single `.write()` call, so
    the underlying stream sees the frame atomically. This matters when the
    same pipe could be written from multiple threads — though our design uses
    one writer thread per pipe, so it's primarily defense in depth.

    ## Parameters
    - `writer` — Any object with a blocking `.write(data)` method.
    - `msg` — A MessagePack-serializable dict.

    ## Raises
    - `FrameTooLargeError` — Serialized payload exceeds `MAX_FRAME_SIZE`.
    """
    payload = msgpack.packb(msg, use_bin_type=True)
    if len(payload) > MAX_FRAME_SIZE:
        raise FrameTooLargeError(
            f"outgoing frame {len(payload)} exceeds max {MAX_FRAME_SIZE}"
        )
    header = struct.pack(_HEADER_FMT, len(payload))
    writer.write(header + payload)


def _read_exact_sync(
    reader: _SyncReader, n: int, *, allow_clean_eof: bool
) -> bytes | None:
    """
    Read exactly `n` bytes, looping over partial reads.

    ## Parameters
    - `reader` — Blocking reader.
    - `n` — Number of bytes to read.
    - `allow_clean_eof` — If `True` and the very first read returns empty,
      returns `None` (peer closed cleanly at boundary). Otherwise any short
      read raises `PeerClosedError`.

    ## Returns
    Exactly `n` bytes, or `None` if `allow_clean_eof=True` and EOF happened
    before any bytes arrived.
    """
    chunks: list[bytes] = []
    remaining = n
    first = True
    while remaining > 0:
        chunk = reader.read(remaining)
        if not chunk:
            if first and allow_clean_eof:
                return None
            raise PeerClosedError(
                f"peer closed mid-frame ({n - remaining}/{n} bytes read)"
            )
        chunks.append(chunk)
        remaining -= len(chunk)
        first = False
    return b"".join(chunks)


# ── Async helpers (used by Unix domain socket path) ──────────────────────────


async def read_frame_async(reader: _AsyncReader) -> dict | None:
    """
    Read one frame from an `asyncio.StreamReader` (or compatible).

    ## Parameters
    - `reader` — Async reader with `readexactly(n)` method.

    ## Returns
    The deserialized message dict, or `None` if the peer closed cleanly at
    a frame boundary.

    ## Raises
    - `PeerClosedError` — Peer closed mid-frame.
    - `FrameTooLargeError` — Length header exceeds `MAX_FRAME_SIZE`.
    - `InvalidFrameError` — Payload is not valid MessagePack.
    """
    import asyncio

    try:
        header = await reader.readexactly(_HEADER_SIZE)
    except asyncio.IncompleteReadError as e:
        if not e.partial:
            return None  # clean EOF at frame boundary
        raise PeerClosedError(
            f"peer closed mid-header ({len(e.partial)}/{_HEADER_SIZE} bytes)"
        ) from e

    (length,) = struct.unpack(_HEADER_FMT, header)
    if length > MAX_FRAME_SIZE:
        raise FrameTooLargeError(
            f"frame length {length} exceeds max {MAX_FRAME_SIZE}"
        )

    try:
        payload = await reader.readexactly(length)
    except asyncio.IncompleteReadError as e:
        raise PeerClosedError(
            f"peer closed mid-payload ({len(e.partial)}/{length} bytes)"
        ) from e

    try:
        return msgpack.unpackb(payload, raw=False)
    except (msgpack.UnpackException, ValueError) as e:
        raise InvalidFrameError(f"msgpack decode failed: {e}") from e


async def write_frame_async(writer: _AsyncWriter, msg: dict) -> None:
    """
    Write one frame to an `asyncio.StreamWriter` (or compatible).

    ## Parameters
    - `writer` — Async writer with `write(data)` and `drain()` methods.
    - `msg` — A MessagePack-serializable dict.

    ## Raises
    - `FrameTooLargeError` — Serialized payload exceeds `MAX_FRAME_SIZE`.

    ## Notes
    The caller is responsible for ensuring only one coroutine writes at a
    time to a given writer (use `asyncio.Lock` or a queue+single-writer-task
    pattern if multiple producers share one writer).
    """
    payload = msgpack.packb(msg, use_bin_type=True)
    if len(payload) > MAX_FRAME_SIZE:
        raise FrameTooLargeError(
            f"outgoing frame {len(payload)} exceeds max {MAX_FRAME_SIZE}"
        )
    header = struct.pack(_HEADER_FMT, len(payload))
    writer.write(header + payload)
    await writer.drain()
