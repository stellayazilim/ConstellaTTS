"""
Shared wire-protocol contract constants.

These strings are the source of truth for what goes over IPC. Any C#
client (`ConstellaTTS.SDK.IPC.StreamEvents`, `Routes.Common.*`) mirrors
these by hand today and will mirror them via codegen later.

Changing a value here is a protocol break — bump an SDK version if you
ever do.

## What lives here (and what doesn't)
- **Here**: contracts shared across *every* module — the lifecycle event
  set every streaming job can emit, the action names every
  `BaseTTSModel` inherits.
- **Not here**: model-specific event types and action names. Each
  module declares those locally, e.g. Chatterbox might emit a
  `"waveform_ready"` event its own router file defines as a constant.
"""

from __future__ import annotations


class StreamEvent:
    """
    Event types emitted over per-job stream pipes.

    The first four are the lifecycle contract: every streaming job
    ultimately emits one of `DONE`, `ERROR`, or `CANCELLED` as its
    terminal event. `CHUNK` is the generic "one unit of output" event.

    `PROGRESS` is optional and informational — models may use it to
    report intermediate state without producing a data chunk.
    """

    # Lifecycle — every streaming job uses these.
    CHUNK = "chunk"
    DONE = "done"
    ERROR = "error"
    CANCELLED = "cancelled"

    # Optional informational events.
    PROGRESS = "progress"


class Action:
    """
    Well-known action names every `BaseTTSModel` subclass exposes.

    Listed here so C# clients can reference `Action.GENERATE` instead
    of a bare `"generate"` literal. Module-specific actions (e.g.
    Chatterbox's voice cloning calls) are not in this list.
    """

    GENERATE = "generate"
    INSPECT = "inspect"
    LOAD = "load"
    UNLOAD = "unload"
    CANCEL = "cancel"
    LIST_JOBS = "list_jobs"


class SystemRoute:
    """Built-in route names that are not tied to a specific model."""

    HEALTH = "health"
    ECHO = "echo"
