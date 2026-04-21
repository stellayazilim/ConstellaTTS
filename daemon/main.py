"""
ConstellaTTS Daemon — IPC echo server (dev stub)

Protocol: length-prefixed MessagePack frames over stdin/stdout.
  Frame: [4-byte LE uint32 = payload length][msgpack bytes]
  Message: { "id": str, "event": str, "data": any | None }

Single-instance enforced via lock file.
"""

import sys
import os
import struct
import atexit
import msgpack

# ── Single instance lock ──────────────────────────────────────────────────────

LOCK_FILE = os.path.join(os.path.dirname(__file__), ".daemon.lock")

def _acquire_lock():
    if os.path.exists(LOCK_FILE):
        try:
            with open(LOCK_FILE) as f:
                pid = int(f.read().strip())
            os.kill(pid, 0)
            print(f"[daemon] already running (pid {pid}), exiting.", file=sys.stderr)
            sys.exit(0)
        except (ValueError, OSError):
            pass  # stale lock

    with open(LOCK_FILE, "w") as f:
        f.write(str(os.getpid()))

def _release_lock():
    try:
        os.remove(LOCK_FILE)
    except OSError:
        pass

atexit.register(_release_lock)

# ── IO helpers ────────────────────────────────────────────────────────────────

_stdin  = sys.stdin.buffer
_stdout = sys.stdout.buffer

def _read_frame() -> dict | None:
    """Read one length-prefixed MessagePack frame. Returns None on EOF."""
    header = _stdin.read(4)
    if len(header) < 4:
        return None
    length = struct.unpack("<I", header)[0]
    payload = _stdin.read(length)
    if len(payload) < length:
        return None
    return msgpack.unpackb(payload, raw=False)

def _write_frame(msg: dict):
    """Write one length-prefixed MessagePack frame."""
    payload = msgpack.packb(msg, use_bin_type=True)
    header  = struct.pack("<I", len(payload))
    _stdout.write(header + payload)
    _stdout.flush()

# ── Message loop ──────────────────────────────────────────────────────────────

def _handle(msg: dict):
    """Echo the message back as-is and log to stderr."""
    print(f"[daemon] recv: {msg}", file=sys.stderr, flush=True)
    _write_frame(msg)

def _loop():
    while True:
        msg = _read_frame()
        if msg is None:
            break
        _handle(msg)

# ── Entry point ───────────────────────────────────────────────────────────────

if __name__ == "__main__":
    _acquire_lock()
    print(f"[daemon] started (pid {os.getpid()})", file=sys.stderr, flush=True)

    try:
        _loop()
    except KeyboardInterrupt:
        pass

    print("[daemon] shutdown", file=sys.stderr, flush=True)
