"""
Route registry — convention-based auto-discovery.

Walks the `modules/` package at startup, imports every file that looks
like a route module, and collects their `Route` exports. Each `Route`
must have a `name` attribute identifying the wire-level route ID; its
public async methods become the callable actions.

Dispatch happens via dotted strings: `"fake.generate"` looks up
`ROUTES["fake"]` and invokes its `.generate(data)` method.

## Two discovery patterns

1. **Single-file routes** — `modules/<something>.route.py`.
   The `.route.py` suffix is the marker. Used for lightweight, stateless
   handlers: `health`, `echo`, anything that doesn't need its own folder.

2. **Folder-based model routes** — `modules/models/<name>/router.py`.
   Used for models that have associated metadata (`model.json`),
   inference code (`inference.py`), and anything else model-specific.

In both cases the module exports a module-level `Route` object:

    # modules/echo.route.py  OR  modules/models/fake/router.py
    class MyRoute:
        name = "myroute"
        async def do_something(self, data): ...

    Route = MyRoute()

## Action methods
Any public (non-underscore-prefixed) coroutine method on the `Route`
object becomes a callable action. Private methods (leading `_`) are
internal and not exposed over IPC.
"""

from __future__ import annotations

import importlib
import importlib.util
import inspect
import logging
from pathlib import Path
from typing import Any, Awaitable, Callable


_log = logging.getLogger(__name__)


#: The global route registry. Populated by `discover()`.
#: Maps `route_name` → route instance (or class with async methods).
ROUTES: dict[str, Any] = {}


#: Type alias — every action is an async callable taking a data dict.
ActionHandler = Callable[[dict], Awaitable[dict]]


# ── Exceptions ────────────────────────────────────────────────────────────────


class RouteError(Exception):
    """Base class for route dispatch errors."""


class UnknownRouteError(RouteError):
    """No route registered for the given `route_name`."""


class UnknownActionError(RouteError):
    """Route exists but has no matching action."""


class InvalidRouteFormatError(RouteError):
    """Route string is not in `route_name.action` format."""


# ── Discovery ─────────────────────────────────────────────────────────────────


def discover(modules_package: str = "modules") -> None:
    """
    Scan the `modules/` package and register every route found.

    ## Parameters
    - `modules_package` — Python package name (default `"modules"`).

    ## Discovery rules
    - Files ending in `.route.py` anywhere under `modules/` are imported.
    - Directories under `modules/models/` containing a `router.py` are
      imported as `modules.models.<dirname>.router`.
    - Files and directories starting with `_` are skipped (internal
      helpers like `_base.py`).

    ## Raises
    - `ValueError` on duplicate route names (caught early, before the
      daemon starts serving).
    """
    package = importlib.import_module(modules_package)
    if not hasattr(package, "__path__"):
        raise RuntimeError(f"{modules_package!r} is not a package")

    root = Path(next(iter(package.__path__)))
    _discover_in(root, modules_package)


def _discover_in(root: Path, package_prefix: str) -> None:
    """Recursively scan a directory for routes."""
    for entry in sorted(root.iterdir()):
        name = entry.name
        if name.startswith("_") or name.startswith("."):
            continue

        if entry.is_file() and name.endswith(".route.py"):
            # Single-file route: modules/foo.route.py → modules.foo_route
            # Python module names can't contain dots, so we use the
            # filesystem path directly with importlib.util.
            _load_route_from_file(entry, dotted_hint=f"{package_prefix}.{name}")
            continue

        if entry.is_dir():
            router_py = entry / "router.py"
            if router_py.is_file():
                # Folder-based route: modules/models/fake/router.py →
                # modules.models.fake.router
                module_dotted = f"{package_prefix}.{name}.router"
                _load_route_from_module(module_dotted)

            # Recurse — e.g. modules/models/ itself is a directory we
            # need to walk into even though it has no router.py of its
            # own.
            _discover_in(entry, f"{package_prefix}.{name}")


def _load_route_from_module(dotted_path: str) -> None:
    """Import a module by dotted path and register its `Route` export."""
    try:
        module = importlib.import_module(dotted_path)
    except Exception as e:
        _log.exception("Failed to import %s: %s", dotted_path, e)
        return
    _register(module, dotted_path)


def _load_route_from_file(path: Path, dotted_hint: str) -> None:
    """
    Import a `*.route.py` file directly from its filesystem path.

    Needed because Python module names can't contain dots, so
    `modules/foo.route.py` isn't importable as `modules.foo.route`. We
    use `importlib.util.spec_from_file_location` to load it under a
    synthetic dotted name instead.
    """
    # Synthesize an importable name: modules/health.route.py → modules.health_route
    synthetic = dotted_hint.replace(".route.py", "_route")
    spec = importlib.util.spec_from_file_location(synthetic, path)
    if spec is None or spec.loader is None:
        _log.error("Could not load spec for %s", path)
        return
    module = importlib.util.module_from_spec(spec)
    try:
        spec.loader.exec_module(module)
    except Exception as e:
        _log.exception("Failed to exec %s: %s", path, e)
        return
    _register(module, str(path))


def _register(module: Any, source: str) -> None:
    """Extract the module's `Route` export and add it to `ROUTES`."""
    route = getattr(module, "Route", None)
    if route is None:
        return  # module has no route export — silently skip

    name = getattr(route, "name", None)
    if not name:
        _log.warning("%s: Route has no `name` attribute, skipping", source)
        return

    if name in ROUTES:
        raise ValueError(
            f"Duplicate route name {name!r}: "
            f"{source} conflicts with existing registration"
        )

    ROUTES[name] = route
    actions = _list_actions(route)
    _log.info("Registered route %r with actions: %s", name, sorted(actions))


def _list_actions(route: Any) -> list[str]:
    """Return the names of public async methods callable as actions."""
    actions = []
    for attr_name in dir(route):
        if attr_name.startswith("_"):
            continue
        attr = getattr(route, attr_name, None)
        if inspect.iscoroutinefunction(attr):
            actions.append(attr_name)
    return actions


# ── Dispatch ──────────────────────────────────────────────────────────────────


async def dispatch(route_str: str, data: dict) -> dict:
    """
    Invoke the handler for `route_str` with `data`.

    ## Parameters
    - `route_str` — Dotted route, e.g. `"fake.generate"`.
    - `data` — Arbitrary dict payload passed to the action handler.

    ## Returns
    The dict returned by the action handler.

    ## Raises
    - `InvalidRouteFormatError` — `route_str` doesn't contain a `.`.
    - `UnknownRouteError` — No route registered with that name.
    - `UnknownActionError` — Route exists but has no matching action.
    - Any exception raised by the action handler itself (propagated).
    """
    if "." not in route_str:
        raise InvalidRouteFormatError(
            f"route {route_str!r} must be in 'name.action' format"
        )

    route_name, action_name = route_str.split(".", 1)

    route = ROUTES.get(route_name)
    if route is None:
        raise UnknownRouteError(f"no route registered for {route_name!r}")

    handler = getattr(route, action_name, None)
    if handler is None or action_name.startswith("_"):
        raise UnknownActionError(
            f"route {route_name!r} has no action {action_name!r}"
        )
    if not inspect.iscoroutinefunction(handler):
        raise UnknownActionError(
            f"{route_str!r} is not an async action"
        )

    return await handler(data)


# ── Introspection helpers ────────────────────────────────────────────────────


def list_routes() -> dict[str, list[str]]:
    """
    Return a snapshot of all registered routes and their actions.

    ## Returns
    Dict mapping `route_name` to list of action names. Used by
    `health.routes` for client-side discovery.
    """
    return {name: _list_actions(route) for name, route in ROUTES.items()}
