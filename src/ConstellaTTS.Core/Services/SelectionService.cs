using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.SDK.UI.Selection;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// Default <see cref="ISelectionService"/> — plain ObservableObject with
/// source-generated property change notifications. No special logic;
/// just holds the currently-edited block and its owning track as a pair.
/// </summary>
public sealed partial class SelectionService : ObservableObject, ISelectionService
{
    [ObservableProperty] private IStageViewModel? _selectedBlock;
    [ObservableProperty] private ITrackViewModel? _selectedTrack;
}
