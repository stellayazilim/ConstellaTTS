"""
Quick end-to-end Python smoke test — bypasses C# to verify the daemon's
pipe server works on its own.

Uses raw CreateFile/ReadFile/WriteFile (not Python's open()) because
Python's built-in file layer does weird buffering on named pipes.
"""
from __future__ import annotations

import ctypes
import os
import struct
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path

import msgpack

if sys.platform != "win32":
    raise SystemExit("Windows only")

_HERE = Path(__file__).resolve().parent


# ── Win32 bindings ───────────────────────────────────────────────────────────

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

GENERIC_READ  = 0x80000000
GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 3
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

CreateFileW = kernel32.CreateFileW
CreateFileW.argtypes = [
    wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
    ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE,
]
CreateFileW.restype = wintypes.HANDLE

ReadFile = kernel32.ReadFile
ReadFile.argtypes = [
    wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p,
]
ReadFile.restype = wintypes.BOOL

WriteFile = kernel32.WriteFile
WriteFile.argtypes = [
    wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p,
]
WriteFile.restype = wintypes.BOOL

CloseHandle = kernel32.CloseHandle
CloseHandle.argtypes = [wintypes.HANDLE]
CloseHandle.restype = wintypes.BOOL


def open_pipe(path: str) -> int:
    h = CreateFileW(
        path,
        GENERIC_READ | GENERIC_WRITE,
        0,              # no sharing
        None,           # no security
        OPEN_EXISTING,
        0,              # no flags (synchronous)
        None,
    )
    if h == INVALID_HANDLE_VALUE:
        raise OSError(f"CreateFile failed, code={ctypes.get_last_error()}")
    return h


def write_all(h: int, data: bytes) -> None:
    buf = (ctypes.c_char * len(data)).from_buffer_copy(data)
    written = wintypes.DWORD(0)
    total = 0
    while total < len(data):
        ok = WriteFile(h, ctypes.byref(buf, total), len(data) - total,
                       ctypes.byref(written), None)
        if not ok:
            raise OSError(f"WriteFile failed, code={ctypes.get_last_error()}")
        total += written.value


def read_exactly(h: int, n: int) -> bytes:
    out = bytearray()
    buf = (ctypes.c_char * n)()
    bytes_read = wintypes.DWORD(0)
    while len(out) < n:
        ok = ReadFile(h, buf, n - len(out), ctypes.byref(bytes_read), None)
        if not ok:
            raise OSError(f"ReadFile failed, code={ctypes.get_last_error()}")
        if bytes_read.value == 0:
            raise EOFError(f"pipe closed, got {len(out)}/{n} bytes")
        out.extend(buf[:bytes_read.value])
    return bytes(out)


# ── Framing ──────────────────────────────────────────────────────────────────

def write_frame(h: int, msg: dict) -> None:
    body = msgpack.packb(msg, use_bin_type=True)
    header = struct.pack("<I", len(body))
    print(f"  writing {len(header) + len(body)} bytes")
    write_all(h, header + body)
    print(f"  write done")


def read_frame(h: int) -> dict:
    print("  reading header...")
    header = read_exactly(h, 4)
    (length,) = struct.unpack("<I", header)
    print(f"  got header, length={length}")
    body = read_exactly(h, length)
    print(f"  got body, {len(body)} bytes")
    return msgpack.unpackb(body, raw=False)


# ── Main ─────────────────────────────────────────────────────────────────────

def spawn_daemon() -> tuple[subprocess.Popen, str]:
    """
    Spawn the daemon and return (process, control_pipe_path).

    The control pipe path is deterministic: `\\.\pipe\constella_<pid>_control`.
    We don't need any stdout/stderr sentinel — we just poll the pipe
    (via CreateFile with retries) until the daemon's listener is up.
    """
    proc = subprocess.Popen(
        [sys.executable, str(_HERE / "main.py")],
        stdin=subprocess.PIPE,
        stdout=subprocess.DEVNULL,   # daemon doesn't use stdout
        stderr=sys.stderr,           # forward logs
        bufsize=0,
    )
    pipe_path = rf"\\.\pipe\constella_{proc.pid}_control"
    return proc, pipe_path


def open_pipe_with_retry(path: str, timeout: float = 15.0) -> int:
    """
    CreateFile the pipe, polling until the server side is ready.

    Windows returns ERROR_FILE_NOT_FOUND (2) if the pipe doesn't exist
    yet, and ERROR_PIPE_BUSY (231) if all server instances are in use.
    Both are retryable.
    """
    deadline = time.monotonic() + timeout
    while True:
        h = CreateFileW(
            path,
            GENERIC_READ | GENERIC_WRITE,
            0, None, OPEN_EXISTING, 0, None,
        )
        if h != INVALID_HANDLE_VALUE:
            return h
        err = ctypes.get_last_error()
        if err in (2, 231) and time.monotonic() < deadline:
            time.sleep(0.05)
            continue
        raise OSError(f"CreateFile failed, code={err}")


def main() -> None:
    proc = None
    try:
        if len(sys.argv) >= 2:
            address = sys.argv[1]
            print(f"Connecting to existing daemon at {address}")
        else:
            print("Spawning daemon...")
            proc, address = spawn_daemon()
            print(f"Daemon pid={proc.pid}, control pipe={address}")

        print(f"\nOpening pipe via CreateFile: {address}")
        h = open_pipe_with_retry(address)
        print(f"Opened, handle={h}")

        try:
            # Test 1
            print("\n[test] echo.ping")
            write_frame(h, {"id": "1", "route": "echo.ping", "data": None})
            resp = read_frame(h)
            print(f"  response: {resp}")
            assert resp["ok"] is True

            # Test 2
            print("\n[test] echo.say")
            write_frame(h, {"id": "2", "route": "echo.say",
                            "data": {"msg": "merhaba", "n": 42}})
            resp = read_frame(h)
            print(f"  response: {resp}")
            assert resp["ok"] is True

            # Test 3
            print("\n[test] nosuch.route")
            write_frame(h, {"id": "3", "route": "nosuch.x", "data": None})
            resp = read_frame(h)
            print(f"  response: {resp}")
            assert resp["ok"] is False

            # Test 4 — streaming via fake.generate
            print("\n[test] fake.generate  (streaming)")
            write_frame(h, {
                "id": "4",
                "route": "fake.generate",
                "data": {"text": "Hello"},
            })
            resp = read_frame(h)
            print(f"  response: {resp}")
            assert resp["ok"] is True
            stream_pipe = resp["data"]["stream_pipe"]
            job_id = resp["data"]["job_id"]
            print(f"  subscribing to stream pipe: {stream_pipe}")

            sh = open_pipe_with_retry(stream_pipe)
            try:
                events = []
                while True:
                    ev = read_frame(sh)
                    print(f"  stream event: {ev}")
                    events.append(ev)
                    if ev["type"] in ("done", "error", "cancelled"):
                        break
                chunks = [e for e in events if e["type"] == "chunk"]
                assert len(chunks) == 5, f"expected 5 chunks, got {len(chunks)}"
                assert "".join(c["data"]["char"] for c in chunks) == "Hello"
                print(f"  ✓ received all 5 chars and a 'done' event")
            finally:
                CloseHandle(sh)

            # Test 5 — list_jobs after completion (should be empty)
            print("\n[test] fake.list_jobs  (post-completion)")
            write_frame(h, {"id": "5", "route": "fake.list_jobs", "data": None})
            resp = read_frame(h)
            print(f"  response: {resp}")
            assert resp["ok"] is True
            assert resp["data"]["jobs"] == [], \
                f"expected no active jobs, got {resp['data']['jobs']}"

            # Test 6 — cancel a running job
            print("\n[test] fake.cancel  (mid-stream)")
            write_frame(h, {
                "id": "6a",
                "route": "fake.generate",
                "data": {"text": "This is a longer text for cancellation"},
            })
            resp = read_frame(h)
            assert resp["ok"] is True
            job_id = resp["data"]["job_id"]
            stream_pipe = resp["data"]["stream_pipe"]
            print(f"  started job {job_id}")

            sh = open_pipe_with_retry(stream_pipe)
            try:
                # Read a couple of chunks then cancel.
                read_frame(sh)
                read_frame(sh)
                print(f"  cancelling job {job_id}")
                write_frame(h, {
                    "id": "6b",
                    "route": "fake.cancel",
                    "data": {"job_id": job_id},
                })
                resp = read_frame(h)
                print(f"  cancel response: {resp}")
                assert resp["ok"] is True
                assert resp["data"]["cancelled"] is True
            finally:
                CloseHandle(sh)

            print("\nAll tests passed!")
        finally:
            CloseHandle(h)

    finally:
        if proc is not None:
            try:
                proc.stdin.close()
                proc.wait(timeout=5)
            except Exception:
                proc.kill()


if __name__ == "__main__":
    main()
