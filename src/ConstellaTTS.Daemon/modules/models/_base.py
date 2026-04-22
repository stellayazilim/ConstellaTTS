"""
Base class for TTS model routes.

Handles shared concerns so each concrete model subclass only needs to
implement the actual inference work:

- Lazy loading (first `generate` triggers `_do_load`, others wait).
- VRAM semaphore (per-model concurrency limit).
- Standard actions: `generate`, `inspect`, `load`, `unload`.
- Job admin: `cancel`, `list_jobs` for any in-flight streaming jobs.
- Transport access: `self._transport` is injected after route discovery
  so subclasses can open `StreamChannel`s for streaming output.

## Lazy loading
Models are not loaded at daemon startup. The first `generate` call triggers
`_do_load` and populates `self._model`; subsequent calls reuse the loaded
weights. An asyncio lock serializes concurrent first-calls so only one
load happens.

After an explicit `unload`, the next `generate` will transparently re-load
the model (same lazy-load path). This default suits the "auto-load on
demand" UX; a stricter "require explicit load" mode can later be gated by
a daemon-level setting that causes `generate` to error if `_model is None`.

## Future: VRAM-aware LRU cache
When more than one model is registered, a global `ModelCache` will sit
above `BaseTTSModel` and decide which models stay resident based on VRAM
budget and recency of use. Hook points are marked with `# LRU:` comments
so the cache can be wired in without refactoring model implementations.

## Concurrency
Each model declares `max_concurrent` — the number of parallel inferences
allowed on its weights. A semaphore enforces this; extra requests queue
up. The value should be tuned per model based on VRAM headroom.

## Streaming jobs
Subclasses that stream output open a `StreamChannel` in `_do_generate`,
register it with `_register_job(channel, task)`, and return
`{"job_id": channel.id, "stream_pipe": channel.endpoint.address}` so the
client can subscribe. The base class's `cancel` and `list_jobs` actions
then operate on that registry.

## Metadata
If the model folder contains a `model.json` alongside `router.py`, its
contents are exposed via `inspect`. Subclasses call `load_metadata(__file__)`
from their router to read it.
"""

from __future__ import annotations

import asyncio
import json
import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from ipc.streaming import StreamChannel
    from ipc.transport import Transport


_log = logging.getLogger(__name__)


# ── Exceptions ────────────────────────────────────────────────────────────────


class JobNotFoundError(Exception):
    """Raised by job admin actions when no job with the given id is active."""


# ── Job registry entry ────────────────────────────────────────────────────────


@dataclass
class _JobRecord:
    """Book-keeping for one in-flight streaming job."""

    channel: "StreamChannel"
    task: asyncio.Task
    started_at: float
    info: dict = field(default_factory=dict)


# ── Base model class ─────────────────────────────────────────────────────────


class BaseTTSModel:
    """
    Abstract base for TTS model routes.

    Subclasses must set `name` (the route identifier) and implement
    `_do_load`, `_do_unload`, `_do_generate`. The public action methods
    (`generate`, `inspect`, `load`, `unload`, `cancel`, `list_jobs`) are
    provided here and should not usually be overridden.

    ## Class attributes (subclass overrides)
    - `name` — Route identifier used in wire protocol (e.g. `"chatterbox_ml"`).
    - `max_concurrent` — Max simultaneous inferences (default 2).
    - `metadata` — Optional dict (typically loaded from `model.json`),
      exposed via `inspect`.

    ## Instance state
    - `_model` — Whatever the subclass's `_do_load` produces (e.g. a
      torch.nn.Module). `None` until loaded.
    - `_semaphore` — Per-model inference throttle.
    - `_load_lock` — Serializes concurrent first-load attempts.
    - `_transport` — Injected by the daemon after discovery; used by
      subclasses to open `StreamChannel`s.
    - `_jobs` — Active streaming jobs keyed by job id.
    """

    name: str = ""                   # subclass must override
    max_concurrent: int = 2          # subclass may override
    metadata: dict[str, Any] = {}    # subclass may override

    def __init__(self) -> None:
        if not self.name:
            raise ValueError(
                f"{type(self).__name__}.name must be set (non-empty string)"
            )

        self._model: Any = None
        self._semaphore = asyncio.Semaphore(self.max_concurrent)
        self._load_lock = asyncio.Lock()

        # Injected post-discovery by the daemon bootstrap.
        self._transport: "Transport | None" = None

        # Active streaming jobs.
        self._jobs: dict[str, _JobRecord] = {}
        self._jobs_lock = asyncio.Lock()

    # ── Injection hooks (called by the daemon post-discovery) ────────────────

    def _set_transport(self, transport: "Transport") -> None:
        """
        Give this route access to the daemon's transport.

        Called once after route discovery. Subclasses use `self._transport`
        to open stream channels in `_do_generate`.
        """
        self._transport = transport

    # ── Public actions (auto-registered as routes) ──

    async def generate(self, data: dict) -> dict:
        """
        Start a generation job.

        Lazy-loads the model on first call, acquires the inference
        semaphore, then delegates to the subclass's `_do_generate`.

        ## Parameters
        - `data` — Model-specific payload (text, language, reference audio,
          etc.). Validated by the subclass.

        ## Returns
        A dict with at least `{"job_id": str, "stream_pipe": str}` —
        the actual chunks are streamed over the named pipe.
        """
        await self._ensure_loaded()
        async with self._semaphore:
            return await self._do_generate(data)

    async def inspect(self, data: dict) -> dict:
        """
        Return static + runtime state of the model. Does not trigger load.

        ## Returns
        Dict with `name`, `loaded`, `max_concurrent`, `active_jobs`,
        `metadata`, and any subclass-specific fields via `_extra_inspect`.
        """
        base = {
            "name": self.name,
            "loaded": self._model is not None,
            "max_concurrent": self.max_concurrent,
            "active_jobs": len(self._jobs),
            "metadata": self.metadata,
        }
        extra = await self._extra_inspect()
        return {**base, **extra}

    async def load(self, data: dict) -> dict:
        """
        Force the model into VRAM now, rather than waiting for the first
        `generate`. Idempotent — already-loaded models return immediately.

        ## Returns
        `{"loaded": True}` once weights are in memory.
        """
        await self._ensure_loaded()
        return {"loaded": True}

    async def unload(self, data: dict) -> dict:
        """
        Release the model's VRAM. If not loaded, no-op.

        ## Returns
        `{"loaded": False}`.

        ## Notes
        Waits for any in-flight inferences to finish (by acquiring all
        semaphore slots) before unloading, so callers don't see a half-
        unloaded model mid-generate.
        """
        if self._model is None:
            return {"loaded": False}

        # Drain in-flight work before unloading.
        async with _acquire_all(self._semaphore, self.max_concurrent):
            if self._model is not None:
                await self._do_unload()
                self._model = None
                _log.info("Unloaded model %s", self.name)

        return {"loaded": False}

    async def cancel(self, data: dict) -> dict:
        """
        Cancel an in-flight streaming job.

        ## Parameters
        - `data.job_id` — The job identifier returned by `generate`.

        ## Returns
        `{"cancelled": True, "job_id": str}`.

        ## Raises
        - `JobNotFoundError` — No active job matches `job_id`.
        """
        job_id = data.get("job_id")
        if not job_id:
            raise ValueError("missing 'job_id' in request data")

        async with self._jobs_lock:
            record = self._jobs.get(job_id)

        if record is None:
            raise JobNotFoundError(f"no active job with id {job_id!r}")

        # Cancel the runner task; its `finally` block will tear down the
        # channel and unregister the job.
        record.task.cancel()

        # Wait up to a short time for teardown so the caller gets an
        # accurate "cancelled" confirmation rather than an inconsistent
        # mid-cleanup state.
        try:
            await asyncio.wait_for(record.task, timeout=2.0)
        except (asyncio.CancelledError, asyncio.TimeoutError, Exception):
            pass

        return {"cancelled": True, "job_id": job_id}

    async def list_jobs(self, data: dict) -> dict:
        """
        Snapshot of this route's active streaming jobs.

        ## Returns
        `{"jobs": [{"id": str, "started_at": float, "info": dict}, ...]}`.
        """
        async with self._jobs_lock:
            jobs = [
                {
                    "id": jid,
                    "started_at": rec.started_at,
                    "info": rec.info,
                }
                for jid, rec in self._jobs.items()
            ]
        return {"jobs": jobs}

    # ── Subclass helper: job registration ────────────────────────────────────

    async def _register_job(
        self,
        channel: "StreamChannel",
        task: asyncio.Task,
        info: dict | None = None,
    ) -> None:
        """
        Record an in-flight streaming job so the admin actions can find it.

        Call from `_do_generate` after creating the channel and spawning
        the runner task. The job is auto-unregistered when the task
        finishes (success, error, or cancel).
        """
        import time
        record = _JobRecord(
            channel=channel,
            task=task,
            started_at=time.time(),
            info=info or {},
        )
        async with self._jobs_lock:
            self._jobs[channel.id] = record

        # Auto-cleanup when the task ends, regardless of outcome.
        task.add_done_callback(
            lambda _t, jid=channel.id: asyncio.create_task(
                self._unregister_job(jid)
            )
        )

    async def _unregister_job(self, job_id: str) -> None:
        """Remove a completed job from the registry. Safe to call twice."""
        async with self._jobs_lock:
            self._jobs.pop(job_id, None)

    # ── Internal load orchestration ──

    async def _ensure_loaded(self) -> None:
        """
        Ensure `self._model` is populated, loading if necessary.

        Uses `_load_lock` to serialize concurrent first-calls — only the
        first coroutine actually calls `_do_load`; the others wait and
        then observe `self._model` already set.
        """
        if self._model is not None:
            # LRU: touch this model in the global cache (when cache exists).
            return  # fast path — already loaded

        async with self._load_lock:
            # Double-check — another coroutine may have loaded while we
            # were waiting for the lock.
            if self._model is not None:
                return

            # LRU: ask the global cache to evict older models to make
            # VRAM room for this one (when cache exists). For now this is
            # unconditional — a single model always fits.

            _log.info("Loading model %s...", self.name)
            self._model = await self._do_load()
            # LRU: register this model as resident in the cache.
            _log.info("Loaded model %s", self.name)

    # ── Subclass hooks (override these) ──

    async def _do_load(self) -> Any:
        """
        Load the model into VRAM and return the loaded object.

        Called once (protected by `_load_lock`). Whatever is returned is
        stored in `self._model` and used by `_do_generate`.

        Implementations should use `asyncio.to_thread` to avoid blocking
        the event loop during the actual torch/file I/O.

        ## Returns
        An opaque model handle — typically a torch.nn.Module.
        """
        raise NotImplementedError

    async def _do_unload(self) -> None:
        """
        Release VRAM held by `self._model`.

        At minimum, clear references so Python GC can collect; also call
        `torch.cuda.empty_cache()` if using torch. Called with no
        inferences in flight (guaranteed by `unload`'s semaphore drain).
        """
        raise NotImplementedError

    async def _do_generate(self, data: dict) -> dict:
        """
        Start a streaming job.

        Called with `self._model` guaranteed loaded and an inference slot
        already acquired from `self._semaphore`. Subclasses should:

        1. Create a `StreamChannel` via `StreamChannel.open(self._transport)`.
        2. Spawn a background task that waits for the client and streams.
        3. Register the task with `await self._register_job(channel, task)`.
        4. Return `{"job_id": channel.id, "stream_pipe": channel.endpoint.address}`.

        ## Parameters
        - `data` — Model-specific payload.

        ## Returns
        Dict with at least `job_id` and `stream_pipe`.
        """
        raise NotImplementedError

    async def _extra_inspect(self) -> dict:
        """
        Optional hook for subclasses to add fields to `inspect` output.

        ## Returns
        Dict merged into the base inspect response. Default is empty.
        """
        return {}


# ── Helpers ───────────────────────────────────────────────────────────────────


def load_metadata(router_file: str) -> dict[str, Any]:
    """
    Load `model.json` from the same directory as the calling `router.py`.

    Use from a model's `router.py` like:

        from modules.models._base import BaseTTSModel, load_metadata

        METADATA = load_metadata(__file__)

        class FakeRoute(BaseTTSModel):
            name = METADATA["name"]
            max_concurrent = METADATA.get("max_concurrent", 2)
            metadata = METADATA

    ## Parameters
    - `router_file` — typically `__file__` passed from the caller.

    ## Returns
    The parsed JSON dict, or an empty dict if `model.json` is missing
    (with a warning log).
    """
    json_path = Path(router_file).resolve().parent / "model.json"
    if not json_path.is_file():
        _log.warning("model.json not found at %s", json_path)
        return {}
    try:
        return json.loads(json_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        _log.error("Failed to load %s: %s", json_path, e)
        return {}


class _acquire_all:
    """
    Async context manager that acquires every slot of a semaphore.

    Used to gate operations that must run when no other holders are
    active (e.g. unloading a model while inferences are in flight).

    ## Notes
    asyncio.Semaphore doesn't expose an "acquire N atomically" method, so
    we acquire one at a time. If any acquire is cancelled mid-way,
    successfully-acquired slots are released again.
    """

    def __init__(self, semaphore: asyncio.Semaphore, n: int) -> None:
        self._semaphore = semaphore
        self._n = n
        self._held = 0

    async def __aenter__(self):
        try:
            for _ in range(self._n):
                await self._semaphore.acquire()
                self._held += 1
        except BaseException:
            # Release anything we got before the failure.
            for _ in range(self._held):
                self._semaphore.release()
            self._held = 0
            raise
        return self

    async def __aexit__(self, *exc):
        for _ in range(self._held):
            self._semaphore.release()
        self._held = 0
