using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// ViewModel for the track list view. Owns the observable track
/// collection and the reorder operation directly. Subscribes to
/// <see cref="IProjectManager.ActiveChanged"/> so opening a project
/// rebuilds the collection from <see cref="IConstellaProject.Tracks"/>;
/// closing one (the manager raising the event with a null project)
/// clears it.
///
/// <para>
/// <b>Empty-state on null.</b> A null active project leaves
/// <see cref="Tracks"/> empty rather than throwing. The DAW can be
/// opened directly without a project for debugging
/// (<see cref="DawDirectBootstrap"/>), and the launcher closes the
/// previous project before opening the next — the in-between window
/// where Active is null is brief, but the rebuild path has to handle
/// it cleanly.
/// </para>
///
/// <para>
/// <b>Palette is the single source of truth for runtime colours.</b>
/// Persistence intentionally drops <c>Color</c> and <c>BlockBg</c>
/// from <see cref="TrackData"/>; the rebuilder picks them from the
/// position-modulo-palette scheme below. Both the Add Track flow and
/// the on-open hydration use the same helper, so a track at index N
/// always lands on <c>Palette[N % Palette.Length]</c> regardless of
/// how it got there.
/// </para>
/// </summary>
public sealed partial class TrackListViewModel : ViewModel
{
    /// <summary>
    /// Accent colour palette cycled through for new and hydrated
    /// tracks. Picks the entry at <c>position % Palette.Length</c> so
    /// adding many tracks (or re-opening a project with many) yields
    /// a stable, repeating rainbow rather than a random wash. Stored
    /// here rather than at the call site so Add Track and project
    /// hydration share one source of truth.
    /// </summary>
    private static readonly (string color, string bg)[] Palette =
    [
        ("#7C6AF7", "#2A2560"),
        ("#60AAFF", "#1A3A5C"),
        ("#FF60A0", "#3A1A3A"),
        ("#50E0FF", "#0F3A4A"),
        ("#D060FF", "#2A153A"),
        ("#4ADE80", "#143A24"),
        ("#FBBF24", "#3A2A10"),
    ];

    /// <summary>
    /// Resolve the palette entry a track at the given position
    /// should use. Exposed so undo-of-remove (which rebuilds a
    /// track at the end of the list via
    /// <see cref="Actions.AddTrackAction"/>'s snapshot path) lands
    /// on the same colour the original add picked. Without a single
    /// public resolver, the action would have to duplicate the
    /// modulo-palette rule and the two could drift independently.
    /// </summary>
    public static (string color, string bg) PaletteAt(int index) =>
        Palette[index % Palette.Length];

    private readonly IProjectManager _projectManager;

    /// <summary>Tracks displayed on the timeline. The canonical list.</summary>
    public ObservableCollection<ITrackViewModel> Tracks { get; } = [];

    public TrackListViewModel(IProjectManager projectManager)
    {
        _projectManager = projectManager;

        // ActiveChanged fires for both load and unload — the rebuild
        // helper handles the null case (clears the collection) so
        // there's no per-event branching here.
        _projectManager.ActiveChanged += OnActiveChanged;

        // Cover the case where a project was already opened by the
        // time this view-model lands in DI (e.g. the launcher opened
        // the project, then resolved this VM as part of the DAW's
        // window construction). The event has already fired and we
        // missed it; sync against the current Active so the DAW comes
        // up populated rather than empty-then-lazily-filled.
        if (_projectManager.Active is not null)
            RebuildFromProject(_projectManager.Active);
    }

    private void OnActiveChanged(object? sender, IConstellaProject? project)
    {
        if (project is null)
        {
            Tracks.Clear();
            return;
        }
        RebuildFromProject(project);
    }

    /// <summary>
    /// Replace <see cref="Tracks"/> with view-models built from the
    /// project's persisted track data. The collection instance is
    /// preserved (Clear-then-Add rather than reassign) because the
    /// view layer holds onto it directly — TrackListView's minimap
    /// caches the reference in <c>BindMinimap</c>, and a swap would
    /// silently leave the minimap pointing at the previous collection.
    ///
    /// <para>
    /// Per-track colours come from <see cref="Palette"/> at the
    /// track's list position, not from the persisted data. See the
    /// class-level remarks on why colours aren't stored.
    /// </para>
    /// </summary>
    private void RebuildFromProject(IConstellaProject project)
    {
        Tracks.Clear();
        for (int i = 0; i < project.Tracks.Count; i++)
        {
            var data    = project.Tracks[i];
            var palette = PaletteAt(i);
            Tracks.Add(new TrackViewModel(i, data, palette.color, palette.bg));
        }
    }

    /// <summary>
    /// Move the track at <paramref name="fromIdx"/> to <paramref name="toIdx"/>
    /// and renumber every track's <see cref="ITrackViewModel.Order"/> so the
    /// sequence stays contiguous 0..N-1. The observable collection's own
    /// Move event drives the UI repaint.
    ///
    /// <para>
    /// <b>This mutates view-state only.</b> The persisted project's
    /// own track list is untouched until a corresponding action's
    /// <c>Persist</c> step runs; the action is responsible for
    /// calling <see cref="IConstellaProject.ReorderTracks"/> and
    /// triggering a save. Splitting it that way keeps the view-model
    /// usable without a project (the DAW can be opened directly for
    /// a debugging session) and keeps undo / redo working through the
    /// existing history machinery.
    /// </para>
    /// </summary>
    public void Reorder(int fromIdx, int toIdx)
    {
        if (fromIdx == toIdx) return;
        if (fromIdx < 0 || fromIdx >= Tracks.Count) return;
        if (toIdx   < 0 || toIdx   >= Tracks.Count) return;

        Tracks.Move(fromIdx, toIdx);

        for (int i = 0; i < Tracks.Count; i++)
            Tracks[i].Order = (byte)i;
    }

    /// <summary>
    /// Append a fresh empty track. Id is assigned monotonically from
    /// <c>Tracks.Count</c>; colour cycles the palette. If
    /// <paramref name="customName"/> is null or empty, a placeholder
    /// "Track N" is used. No blocks are seeded — the track starts
    /// empty and the user draws into it.
    ///
    /// <para>
    /// Exposed as both a public method (for programmatic use from other
    /// VMs like a future project loader) and an <c>AddTrackCommand</c>
    /// (source-generated by <see cref="RelayCommandAttribute"/>) so XAML
    /// buttons can bind directly. The command-invoked path uses the
    /// default placeholder; the view layer's Add Track dialog calls the
    /// parameterised overload with the user's typed name.
    /// </para>
    ///
    /// <para>
    /// <b>View-state only.</b> Same caveat as <see cref="Reorder"/>:
    /// this adds a VM row but doesn't touch the persisted project.
    /// The action layer (Alt-tur 3 in the persistence plan) wraps
    /// this call and adds the corresponding
    /// <see cref="IConstellaProject.AddTrack"/> + save.
    /// </para>
    /// </summary>
    [RelayCommand]
    public void AddTrack() => AddTrack(null);

    /// <inheritdoc cref="AddTrack()" />
    public void AddTrack(string? customName)
    {
        var idx     = Tracks.Count;
        var palette = PaletteAt(idx);

        var name = string.IsNullOrWhiteSpace(customName)
            ? $"Track {idx + 1}"
            : customName.Trim();

        var vm = new TrackViewModel(idx, name, palette.color, palette.bg)
        {
            Order = (byte)idx,
        };
        Tracks.Add(vm);
    }

    /// <summary>
    /// Remove a track (and all its blocks). No-op if the track is not in
    /// the collection. Does not re-id remaining tracks — Id is stable per
    /// track for its lifetime; only <see cref="ITrackViewModel.Order"/> is
    /// renumbered to keep the sequence contiguous.
    /// </summary>
    public void RemoveTrack(ITrackViewModel track)
    {
        if (!Tracks.Remove(track)) return;
        for (int i = 0; i < Tracks.Count; i++)
            Tracks[i].Order = (byte)i;
    }

    /// <summary>
    /// Insert an already-built track view-model at the given index.
    /// Used by the undo path of <see cref="Actions.RemoveTrackAction"/>
    /// to put a removed track back where it was, not at the end.
    /// Renumbers <see cref="ITrackViewModel.Order"/> across the list
    /// so the contiguous 0..N-1 invariant holds. Out-of-range indices
    /// are clamped, matching <see cref="IConstellaProject.InsertTrack"/>'s
    /// permissive contract.
    /// </summary>
    public void InsertTrack(int index, ITrackViewModel track)
    {
        if (index < 0)           index = 0;
        if (index > Tracks.Count) index = Tracks.Count;

        Tracks.Insert(index, track);
        for (int i = 0; i < Tracks.Count; i++)
            Tracks[i].Order = (byte)i;
    }
}
