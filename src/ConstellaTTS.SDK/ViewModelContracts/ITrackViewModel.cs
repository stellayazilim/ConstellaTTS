using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;

namespace ConstellaTTS.SDK.ViewModelContracts;

/// <summary>
/// Drop target indicator for drag-and-drop operations on a track row.
/// </summary>
public enum DropIndicator
{
    None,
    Top,
    Bottom
}

/// <summary>
/// Contract for a track view model. The binding surface used by XAML and
/// the DI registration target — plugins may replace the default
/// implementation with their own.
/// </summary>
public interface ITrackViewModel : INotifyPropertyChanged
{
    /// <summary>Unique track index.</summary>
    int Id { get; }

    /// <summary>
    /// Display name (e.g. "Narrator"). Mutable so the user can rename
    /// inline from the track header; bound two-way from the rename TextBox.
    /// </summary>
    string Name { get; set; }

    /// <summary>Accent color (hex) — used for track header strip and section labels.</summary>
    string Color { get; }

    /// <summary>
    /// Dark tinted block background (hex). Read from the underlying domain
    /// track. Used when new blocks are created on this row so they match
    /// the track's palette without any hardcoded mapping.
    /// </summary>
    string BlockBg { get; }

    /// <summary>
    /// Display order on the timeline. Kept on the VM so it survives the
    /// eventual MessagePack round-trip; the visible order in XAML still comes
    /// from the observable collection's position, but Order is the authority
    /// on disk. Rewritten 0..N-1 after every reorder.
    /// </summary>
    byte Order { get; set; }

    /// <summary>
    /// True while the user is actively dragging this track. Driven by the
    /// drag code-behind; XAML binds to it via a style class to fade the
    /// track to low opacity, leaving a "ghost" in the original slot.
    /// </summary>
    bool IsDragging { get; set; }

    /// <summary>
    /// True while the user is inline-renaming this track. View toggles
    /// a label↔TextBox swap based on this flag. Right-click on the
    /// header enters edit mode; TextBox LostFocus / Enter / Escape exit.
    /// </summary>
    bool IsEditing { get; set; }

    /// <summary>Current drop-target indicator state.</summary>
    DropIndicator DropIndicator { get; set; }

    /// <summary>Border thickness reflecting <see cref="DropIndicator"/> — bind to Border.BorderThickness.</summary>
    Thickness IndicatorBorderThickness { get; }

    /// <summary>
    /// Brush used for the drop indicator border. Set during a drag to the
    /// dragged track's color (so the indicator visually belongs to the
    /// incoming track, not the host). Null clears it.
    /// </summary>
    IBrush? IndicatorBrush { get; set; }

    /// <summary>
    /// Timeline blocks on this track — typed to <see cref="IStageViewModel"/>
    /// so the collection can hold both stages (annotations) and sections
    /// (TTS-generating blocks) polymorphically. XAML uses two DataTemplates
    /// keyed by the concrete interface type.
    /// </summary>
    ObservableCollection<IStageViewModel> Sections { get; }
}
