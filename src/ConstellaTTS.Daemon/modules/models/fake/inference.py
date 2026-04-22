"""
Fake TTS inference — pure Python, no external ML dependency.

Emits input text one character at a time with a small delay between
characters, so the streaming pipeline (chunked output over a job pipe)
can be exercised without any real model load or CUDA involvement.
"""

from __future__ import annotations

import asyncio
from typing import AsyncIterator


#: Seconds between emitted characters. Tuned to feel like real-time
#: streaming without being so slow the tests drag.
_CHAR_DELAY = 0.05


class FakeEngine:
    """
    A minimal streaming "engine" that yields characters with delays.

    Used by `FakeRoute` to simulate generating audio chunks. Real TTS
    engines will expose the same `generate(text, ...) -> AsyncIterator`
    shape but yield byte chunks of PCM audio instead of characters.
    """

    async def generate(self, text: str) -> AsyncIterator[str]:
        """
        Yield each character of `text` with a short delay.

        ## Parameters
        - `text` — The string to "generate".

        ## Yields
        One character at a time, prefixed by a `_CHAR_DELAY` sleep.
        """
        for ch in text:
            await asyncio.sleep(_CHAR_DELAY)
            yield ch
