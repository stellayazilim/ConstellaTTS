"""
Fake TTS route — streams one character at a time.

Used to exercise the streaming pipeline (control pipe → stream pipe →
chunks → done) without any real ML work. The client calls
`fake.generate`, gets back a job id and stream pipe path, subscribes to
that pipe, and receives one `chunk` event per character followed by a
`done` event.
"""

from __future__ import annotations

import asyncio
import logging

from ipc.protocol import StreamEvent
from ipc.streaming import StreamChannel
from modules.models._base import BaseTTSModel, load_metadata
from modules.models.fake.inference import FakeEngine


_log = logging.getLogger(__name__)


METADATA = load_metadata(__file__)


class FakeRoute(BaseTTSModel):
    """Route for the fake development model."""

    name = METADATA.get("name", "fake")
    max_concurrent = METADATA.get("max_concurrent", 4)
    metadata = METADATA

    #: Queue capacity for each stream. Small by design — lets us verify
    #: backpressure with fast producers during development.
    _STREAM_CAPACITY = 16

    def __init__(self) -> None:
        super().__init__()
        self._engine: FakeEngine | None = None

    # ── BaseTTSModel hooks ──

    async def _do_load(self) -> FakeEngine:
        # No real weights. A small sleep simulates load latency so UI
        # spinners on the C# side have something to render.
        await asyncio.sleep(0.2)
        self._engine = FakeEngine()
        return self._engine

    async def _do_unload(self) -> None:
        self._engine = None

    async def _do_generate(self, data: dict) -> dict:
        """
        Open a stream channel and spawn the producer task.

        The producer waits for the client to subscribe, then emits one
        `chunk` event per character with a short delay between them.
        """
        if self._transport is None:
            raise RuntimeError(
                "fake route has no transport; was _set_transport called?"
            )

        text = str(data.get("text", "Hello"))

        channel = await StreamChannel.open(
            self._transport,
            capacity=self._STREAM_CAPACITY,
        )

        task = asyncio.create_task(
            self._run_stream(channel, text),
            name=f"fake-stream-{channel.id}",
        )

        await self._register_job(
            channel,
            task,
            info={"text_length": len(text)},
        )

        return {
            "job_id": channel.id,
            "stream_pipe": channel.endpoint.address,
            "chars": len(text),
        }

    # ── Internal ─────────────────────────────────────────────────────────────

    async def _run_stream(self, channel: StreamChannel, text: str) -> None:
        """
        Producer body — wait for subscribe, emit chunks, emit done.

        Runs as its own task so `_do_generate` can return the job info
        to the client without waiting for the whole stream to finish.
        """
        assert self._engine is not None

        try:
            # Give the client a window to subscribe. If they never do,
            # the stream task stays parked on wait_for_client; `close()`
            # during daemon shutdown will cancel it cleanly.
            await channel.wait_for_client(timeout=10.0)
            async for ch in self._engine.generate(text):
                await channel.emit(StreamEvent.CHUNK, {"char": ch})
            await channel.emit(StreamEvent.DONE, {"total": len(text)})

        except asyncio.CancelledError:
            # Admin cancelled us; try to tell the client why before we go.
            try:
                await channel.emit(StreamEvent.CANCELLED, {})
            except Exception:
                pass
            raise

        except Exception as e:
            _log.exception("fake stream %s failed", channel.id)
            try:
                await channel.emit(StreamEvent.ERROR, {"message": str(e)})
            except Exception:
                pass

        finally:
            await channel.close()


#: Module-level export picked up by the registry.
Route = FakeRoute()
