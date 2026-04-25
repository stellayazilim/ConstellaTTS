using System.ComponentModel;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.SDK.UI.Selection;

/// <summary>
/// Single source of truth for "which block the user is currently editing."
/// The timeline view writes here when a block is clicked or just created;
/// the block-editor overlay reads here to know whether and what to render.
///
/// Selection is currently single-block — no multi-select. SelectedTrack is
/// stored alongside SelectedBlock because the editor overlay positions
/// itself relative to the track row the block lives on; without the track
/// reference the view would have to search back up the collection, which
/// is both wasted work and fragile against future reorder semantics.
///
/// Both properties are mutable observables; consumers bind via
/// <see cref="INotifyPropertyChanged"/> and react to changes. Clearing
/// selection (e.g. Esc, click on empty space) is done by assigning null
/// to either property — the view treats null-block as "hide the overlay".
/// </summary>
public interface ISelectionService : INotifyPropertyChanged
{
    /// <summary>The block currently being edited, or null if nothing is selected.</summary>
    IStageViewModel? SelectedBlock { get; set; }

    /// <summary>The track that owns <see cref="SelectedBlock"/>. Null when no selection.</summary>
    ITrackViewModel? SelectedTrack { get; set; }
}
