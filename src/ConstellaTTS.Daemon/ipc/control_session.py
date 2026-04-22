"""
Control session — handles one connected control pipe.

For each accepted control connection, this coroutine runs a read-dispatch-
write loop until the peer disconnects:

    while connection open:
        msg = await read_frame(reader)
        response = await router.dispatch(msg["route"], msg["data"])
        await write_frame(writer, {"id": msg["id"], ...response})

Response envelope:

    {
        "id": <echoed from request>,
        "ok": True,
        "data": <handler's return dict>
    }

Error envelope:

    {
        "id": <echoed from request>,
        "ok": False,
        "error": {
            "type": <exception class name>,
            "message": <str(exception)>
        }
    }
"""

from __future__ import annotations

import asyncio
import logging

from ipc import framing
from ipc.framing import FramingError
from router import registry


_log = logging.getLogger(__name__)


async def run_control_session(
    reader: asyncio.StreamReader,
    writer: asyncio.StreamWriter,
) -> None:
    """
    Run the request/response loop for one control connection.

    ## Parameters
    - `reader` — Async byte reader satisfying the `Reader` protocol.
    - `writer` — Async byte writer satisfying the `Writer` protocol.

    ## Notes
    - Returns when the peer disconnects cleanly or a framing error occurs.
    - Each incoming request is dispatched in its own task so slow handlers
      don't block the read loop.
    - Responses are serialized through `_writer_lock` because multiple
      handler tasks may produce responses concurrently.
    """
    writer_lock = asyncio.Lock()

    async def send(msg: dict) -> None:
        async with writer_lock:
            await framing.write_frame_async(writer, msg)

    while True:
        try:
            msg = await framing.read_frame_async(reader)
        except FramingError as e:
            _log.warning("framing error on control session: %s", e)
            break
        except (ConnectionError, OSError) as e:
            _log.debug("control session connection error: %s", e)
            break

        if msg is None:
            _log.debug("control session peer closed cleanly")
            break

        _log.info("received request: %r", msg)

        # Spawn handler in its own task — don't block the read loop.
        asyncio.create_task(
            _handle_one(msg, send),
            name=f"ctrl-req-{msg.get('id', '?')}",
        )

    _log.debug("control session exiting")


async def _handle_one(msg: dict, send) -> None:
    """
    Dispatch a single request and send back the response envelope.

    ## Parameters
    - `msg` — Decoded request: `{"id": ..., "route": ..., "data": ...}`.
    - `send` — Async callable that writes a response dict back.
    """
    req_id = msg.get("id")
    route = msg.get("route")
    data = msg.get("data", {}) or {}

    if not isinstance(route, str):
        await send(_error_response(req_id, "BadRequest", "missing 'route'"))
        return

    try:
        result = await registry.dispatch(route, data)
    except registry.RouteError as e:
        _log.info("dispatch failed: %s", e)
        await send(_error_response(req_id, type(e).__name__, str(e)))
        return
    except Exception as e:
        _log.exception("handler %s raised", route)
        await send(_error_response(req_id, type(e).__name__, str(e)))
        return

    await send({"id": req_id, "ok": True, "data": result})


def _error_response(req_id, err_type: str, message: str) -> dict:
    """Build a standard error envelope."""
    return {
        "id": req_id,
        "ok": False,
        "error": {"type": err_type, "message": message},
    }
