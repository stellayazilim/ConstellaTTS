using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Default track view model. Plugins may replace via a custom
/// <see cref="ITrackViewModel"/> implementation registered in DI.
///
/// <para>
/// <c>Id</c>, <c>Color</c>, <c>BlockBg</c> are captured at construction
/// and stay fixed for the VM's lifetime — identity and accent are
/// stable. <c>Name</c> is mutable (user can rename inline) and
/// <c>IsEditing</c> is pure view state driving the rename
/// label↔TextBox swap.
/// </para>
///
/// <para>
/// <b>Two construction shapes.</b> The primitive-arg constructor is
/// what UI add-track flows use (the dialog produces a name; the
/// caller picks an Id and a palette entry). The <see cref="TrackData"/>
/// constructor is the hydration path — <see cref="TrackListViewModel"/>
/// rebuilds its observable collection from <c>project.Tracks</c> on
/// project open and per-row palette assignment is deterministic
/// (position-modulo-palette), so the runtime palette argument is
/// supplied by the rebuilder rather than guessed by the VM.
/// </para>
///
/// <para>
/// <b>Colours are reapplied on hydration, not stored.</b> The
/// persisted <see cref="TrackData"/> doesn't carry colours; the
/// rebuilder picks them from the palette using the track's list
/// position. This means re-opening the same project under a
/// different palette restripes the timeline cleanly without fighting
/// stored colour values, which is the explicit design choice from
/// the persistence plan.
/// </para>
/// </summary>
public sealed partial class TrackViewModel : ObservableObject, ITrackViewModel
{
    /// <summary>
    /// Primitive constructor — used by the UI's Add Track flow, where
    /// the caller already knows the Id, name, and palette entry it
    /// wants and there is no on-disk representation yet.
    /// </summary>
    public TrackViewModel(int id, string name, string color, string blockBg)
    {
        Id      = id;
        Color   = color;
        BlockBg = blockBg;
        _name   = name;
    }

    /// <summary>
    /// Hydration constructor — used by <see cref="TrackListViewModel"/>
    /// when rebuilding from <see cref="IConstellaProject.Tracks"/>.
    /// The palette entry is supplied by the rebuilder (position-modulo
    /// scheme), and the track's blocks are materialised in the same
    /// pass so a freshly hydrated VM lands in the UI with everything
    /// already populated.
    ///
    /// <para>
    /// <see cref="TrackData.Order"/> is read into the VM's
    /// <see cref="Order"/> as a starting value but the rebuilder
    /// renumbers afterwards, so a malformed manifest with gaps or
    /// duplicates settles to contiguous 0..N-1 regardless.
    /// </para>
    /// </summary>
    public TrackViewModel(int id, TrackData data, string color, string blockBg)
    {
        Id      = id;
        Color   = color;
        BlockBg = blockBg;
        _name   = data.Name;
        _order  = data.Order;

        // Materialise blocks in the on-disk list order. The list
        // order is the contract between the persisted blocks and
        // the VM's <see cref="Sections"/> collection — a block at
        // index N in <see cref="TrackData.Blocks"/> lands at index
        // N in <see cref="Sections"/>, and the block ID
        // <c>{trackName}/{N}</c> resolves to the same logical block
        // on both sides. Sorting by StartSec here would shuffle that
        // pairing and break action persist code that derives a
        // block's ID from <c>Sections.IndexOf(...)</c>.
        //
        // Time-order display is the timeline panel's job, not the
        // collection's: <c>TimelineItemsPanel</c> arranges children
        // by StartSec at render time, so the visual ordering is
        // independent of the underlying collection ordering. The
        // persisted list itself is generally written in time order
        // (blocks are appended as the user draws them, and moves
        // round-trip through remove+add at the same index), but a
        // hand-edited or tooling-produced manifest with reordered
        // entries still hydrates faithfully.
        foreach (var block in data.Blocks)
        {
            var vm = block.Kind switch
            {
                BlockKind.Section => (IStageViewModel)BuildSection(block, color, blockBg),
                _                 => BuildStage  (block, color, blockBg),
            };
            Sections.Add(vm);
        }
    }

    private static StageViewModel BuildStage(BlockData data, string accent, string bg) => new()
    {
        Label       = data.Label,
        StartSec    = data.StartSec,
        DurationSec = data.DurationSec,
        AccentColor = accent,
        Bg          = bg,
    };

    private static SectionViewModel BuildSection(BlockData data, string accent, string bg) => new()
    {
        Label          = data.Label,
        StartSec       = data.StartSec,
        DurationSec    = data.DurationSec,
        AccentColor    = accent,
        Bg             = bg,

        // Section parameters fall back to the same defaults as a
        // freshly-drawn section if the persisted POCO somehow has
        // them null on a Section-kind row (a malformed or
        // partially-migrated manifest). Default values mirror
        // SectionViewModel's field initialisers — keeping the two in
        // sync means a hydrated section is indistinguishable from
        // one created in this session, with or without persisted
        // overrides.
        Emotion        = data.Emotion        ?? 50,
        Temperature    = data.Temperature    ?? 0.7,
        Seed           = data.Seed           ?? 0,
        SeedMode       = data.SeedMode       ?? SeedAdvanceMode.Fixed,
        EngineId       = data.EngineId       ?? string.Empty,
        VoiceSampleRef = data.VoiceSampleRef,

        // Loaded sections are always Dirty: we can't tell from the
        // manifest alone whether the cached audio (if any) is up to
        // date with these parameters, so we force a regenerate-flag
        // on open and let the first generate-or-skip decide. Storing
        // Dirty would only mislead — the parameter set is what we
        // trust, and Dirty is a derived "needs work" hint.
        Dirty          = true,
    };

    public int    Id      { get; }
    public string Color   { get; }
    public string BlockBg { get; }

    [ObservableProperty] private string _name;

    [ObservableProperty] private byte _order;

    /// <summary>
    /// True while this track is being actively dragged. Set by the
    /// drag controller; XAML binds the class <c>drag-ghost</c> to this
    /// flag so the in-list row fades out, leaving the cursor-attached
    /// preview as the only visible representation.
    /// </summary>
    [ObservableProperty] private bool _isDragging;

    /// <summary>
    /// True while the user is inline-renaming this track via a right-click
    /// on the header. XAML swaps the Name TextBlock for a TextBox while
    /// this is set.
    /// </summary>
    [ObservableProperty] private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicatorBorderThickness))]
    private DropIndicator _dropIndicator = DropIndicator.None;

    public Avalonia.Thickness IndicatorBorderThickness => DropIndicator switch
    {
        DropIndicator.Top    => new Avalonia.Thickness(0, 2, 0, 0),
        DropIndicator.Bottom => new Avalonia.Thickness(0, 0, 0, 2),
        _                    => new Avalonia.Thickness(0)
    };

    /// <summary>
    /// Set during a drag to the dragged track's colour; null otherwise.
    /// BorderBrush binding stays transparent when null — no leaked frame.
    /// </summary>
    [ObservableProperty] private IBrush? _indicatorBrush;

    /// <summary>
    /// Timeline blocks for this track. Typed to <see cref="IStageViewModel"/>
    /// so the collection can hold both stages and sections. Plugins can
    /// substitute their own implementations; defaults are
    /// <see cref="StageViewModel"/> and <see cref="SectionViewModel"/>.
    /// </summary>
    public ObservableCollection<IStageViewModel> Sections { get; } = [];
}
