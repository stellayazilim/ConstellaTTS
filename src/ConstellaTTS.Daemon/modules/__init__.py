"""
Route modules — anything that registers a handler with the router.

## Layout
- `*.route.py` — single-file route (system-level, stateless).
  Example: `health.route.py`, `echo.route.py`.

- `models/<name>/router.py` — model-backed route with its own folder.
  Contains `model.json` (metadata), `inference.py` (ML code), and
  `router.py` (the IPC-facing wrapper).

Both patterns are picked up by `router.registry.discover()` automatically;
a module just needs to export a module-level `Route` object whose `name`
attribute matches the wire-level route identifier.
"""
