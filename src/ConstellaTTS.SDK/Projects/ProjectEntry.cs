using MessagePack;

namespace ConstellaTTS.SDK.Projects;

/// <summary>
/// One row in the application's project registry — the persisted list
/// of folders the user has previously opened or created. The registry
/// drives the launcher's "recent projects" panel; the entry itself is
/// the minimum the launcher needs to render a row and reopen the
/// project, with no data loaded from the project folder yet.
///
/// <para>
/// <b>What lives here vs. what doesn't.</b> Anything that can only be
/// known by reading the project's manifest (track count, block count,
/// duration, tags) is intentionally absent. Those values would either
/// require pre-loading every registry entry on launcher boot — slow
/// and unnecessary — or go stale the moment the project is edited
/// outside the launcher's view. The registry stays cheap and writes
/// stay infrequent (open / create / pin / remove).
/// </para>
///
/// <para>
/// <b>Persistence shape.</b> Serialized to the registry file with
/// MessagePack using stable integer keys. Reordering parameters in
/// the primary constructor would break compatibility with existing
/// registry files; new fields take fresh key numbers and slot in at
/// the end of the parameter list.
/// </para>
/// </summary>
/// <param name="Name">User-facing display name for the row.</param>
/// <param name="Path">Absolute path to the project folder on disk.</param>
/// <param name="LastOpened">Timestamp of the most recent open. Used for the row's "2 hours ago" subtitle and for default sort order.</param>
/// <param name="Pinned">When true, the row is rendered with a star and stays at the top of the list regardless of <see cref="LastOpened"/>.</param>
[MessagePackObject]
public sealed record ProjectEntry(
    [property: Key(0)] string         Name,
    [property: Key(1)] string         Path,
    [property: Key(2)] DateTimeOffset LastOpened,
    [property: Key(3)] bool           Pinned);
