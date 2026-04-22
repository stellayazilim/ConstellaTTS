"""
ConstellaTTS daemon entry point.

Responsibilities:
  1. Load daemon-local Python path so `models`, `router`, etc. import.
  2. Enforce single-instance via a lock file.
  3. Configure logging: everything to a rotating file in `logs/`,
     INFO/DEBUG to stdout, WARNING and above to stderr.
  4. Run route discovery against the `modules/` package.
  5. Start the IPC transport and open the `control` channel.
  6. Run the asyncio event loop until terminated.

The daemon exits when its stdin closes (parent C# process terminated) or
on SIGINT/SIGTERM. The control pipe path is deterministic
(`\\.\pipe\constella_<pid>_control`), so the host derives it from
`Process.Id` without any explicit announcement.
"""

from __future__ import annotations

import asyncio
import logging
import os
import signal
import sys
from datetime import datetime
from pathlib import Path


# ── Python path bootstrap (before any `from ipc import ...`) ─────────────────

_DAEMON_DIR = Path(__file__).resolve().parent
if str(_DAEMON_DIR) not in sys.path:
    sys.path.insert(0, str(_DAEMON_DIR))


# ── Single-instance lock ──────────────────────────────────────────────────────

_LOCK_FILE = _DAEMON_DIR / ".daemon.lock"


def _acquire_lock() -> None:
    """Exit if another daemon with a live PID is already running."""
    if _LOCK_FILE.exists():
        try:
            existing_pid = int(_LOCK_FILE.read_text().strip())
            os.kill(existing_pid, 0)  # signal 0 = "are you alive?"
            print(
                f"[daemon] already running (pid {existing_pid}), exiting.",
                file=sys.stderr,
            )
            sys.exit(0)
        except (ValueError, OSError):
            pass  # stale lock, overwrite

    _LOCK_FILE.write_text(str(os.getpid()))


def _release_lock() -> None:
    try:
        _LOCK_FILE.unlink()
    except OSError:
        pass


# ── Logging ───────────────────────────────────────────────────────────────────

_LOG_DIR = _DAEMON_DIR / "logs"

#: How many old log files to keep. Older ones are deleted at startup.
_LOG_RETENTION = 10


def _setup_logging() -> None:
    """
    Configure root logger with three destinations:

    - **File handler** (`logs/daemon_<pid>_<timestamp>.log`): every level,
      captures everything for post-mortem debugging.
    - **Stdout handler**: INFO / DEBUG level — normal operational logs.
      Routed to stdout following the Unix convention that stderr is for
      *problems*, not for chatter.
    - **Stderr handler**: WARNING and above — anything the operator
      actually needs to notice.

    Set `DAEMON_DEBUG=1` to lower the stdout floor to DEBUG.
    """
    _LOG_DIR.mkdir(exist_ok=True)
    _cleanup_old_logs()

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    log_file = _LOG_DIR / f"daemon_{os.getpid()}_{timestamp}.log"

    root = logging.getLogger()
    root.setLevel(logging.DEBUG)  # handlers decide what's emitted where

    fmt = logging.Formatter(
        "%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%H:%M:%S",
    )

    # File handler — full detail, always DEBUG.
    file_handler = logging.FileHandler(log_file, encoding="utf-8")
    file_handler.setLevel(logging.DEBUG)
    file_handler.setFormatter(fmt)
    root.addHandler(file_handler)

    # Stdout handler — INFO/DEBUG, but capped at WARNING so we don't
    # double-emit when stderr also picks up the record.
    stdout_handler = logging.StreamHandler(sys.stdout)
    stdout_handler.setLevel(
        logging.DEBUG if os.environ.get("DAEMON_DEBUG") else logging.INFO
    )
    stdout_handler.addFilter(lambda record: record.levelno < logging.WARNING)
    stdout_handler.setFormatter(fmt)
    root.addHandler(stdout_handler)

    # Stderr handler — WARNING and above only. Host treats anything on
    # stderr as a real problem worth surfacing.
    stderr_handler = logging.StreamHandler(sys.stderr)
    stderr_handler.setLevel(logging.WARNING)
    stderr_handler.setFormatter(fmt)
    root.addHandler(stderr_handler)

    logging.getLogger("daemon").info("Logging to file: %s", log_file)


def _cleanup_old_logs() -> None:
    """Delete all but the most recent `_LOG_RETENTION` log files."""
    try:
        logs = sorted(
            _LOG_DIR.glob("daemon_*.log"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
        for old in logs[_LOG_RETENTION:]:
            try:
                old.unlink()
            except OSError:
                pass
    except OSError:
        pass


# ── Stdin watcher (graceful shutdown when parent dies) ────────────────────────

async def _watch_stdin_close(shutdown_event: asyncio.Event) -> None:
    """
    Trigger shutdown when stdin hits EOF.

    When the parent C# process terminates, our stdin gets closed — that's
    our cue that nobody's listening anymore. We drain stdin in a background
    thread and signal shutdown when it returns nothing.
    """
    loop = asyncio.get_running_loop()

    def wait_for_eof() -> None:
        try:
            while True:
                chunk = sys.stdin.buffer.read(4096)
                if not chunk:
                    return
        except (OSError, ValueError):
            return

    await loop.run_in_executor(None, wait_for_eof)
    shutdown_event.set()


# ── Main ──────────────────────────────────────────────────────────────────────

async def _main() -> None:
    from ipc.control_session import run_control_session
    from ipc.transport import Transport
    from router import registry

    log = logging.getLogger("daemon")

    # Discover routes. Walks the `modules/` package and registers every
    # `*.route.py` file and `modules/models/*/router.py` it finds.
    registry.discover("modules")
    log.info("Registered routes: %s", sorted(registry.ROUTES.keys()))

    # Start transport.
    transport = Transport.create()
    control_endpoint = await transport.listen("control", run_control_session)

    # Inject the transport into any route that wants it (used for opening
    # per-job StreamChannels). Routes that don't need it simply don't
    # implement `_set_transport`.
    for route in registry.ROUTES.values():
        setter = getattr(route, "_set_transport", None)
        if callable(setter):
            setter(transport)

    # The pipe path is deterministic (derived from our PID), so the C#
    # host doesn't need an explicit "ready" signal — it just polls the
    # pipe path via NamedPipeClientStream.ConnectAsync(timeout), which
    # waits for the server side to start accepting. This keeps the
    # protocol completely on the pipe: stdout and stderr stay for logs.
    log.info("Daemon ready on %s", control_endpoint.address)

    # Install shutdown triggers.
    shutdown_event = asyncio.Event()

    def _signal_shutdown(signum, frame):
        log.info("received signal %d, shutting down", signum)
        shutdown_event.set()

    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            signal.signal(sig, _signal_shutdown)
        except (ValueError, OSError):
            pass  # some signals not available on all platforms

    # Watch stdin so parent-death triggers shutdown too.
    stdin_task = asyncio.create_task(
        _watch_stdin_close(shutdown_event), name="stdin-watcher"
    )

    # Block until shutdown.
    await shutdown_event.wait()

    log.info("Shutting down transport...")
    await transport.close()

    stdin_task.cancel()
    try:
        await stdin_task
    except asyncio.CancelledError:
        pass

    log.info("Daemon stopped.")


def main() -> None:
    _acquire_lock()
    _setup_logging()
    try:
        asyncio.run(_main())
    except KeyboardInterrupt:
        pass
    finally:
        _release_lock()


if __name__ == "__main__":
    main()
