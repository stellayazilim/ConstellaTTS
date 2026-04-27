using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.SDK.UI.Tools;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Default <see cref="IToolModeService"/> implementation. Observable so
/// views can bind button state directly to Tool / CreateType changes.
///
/// <para>
/// <b>Defaults: Select tool with Section as the staged create sub-type.</b>
/// Select is the safer landing spot — a stray click on the canvas just
/// clears selection rather than dropping a phantom block onto a track.
/// Users opt into Create deliberately (Ctrl+drag transient override or
/// the toolbar toggle), and at that point CreateType already remembers
/// Section as the kind to draw, which is the most common starting
/// intent when opening a fresh project.
/// </para>
///
/// EffectiveTool / EffectiveCreateType are computed from (Preview ?? committed);
/// we manually raise change notifications for them from the source generator
/// partial hooks so bindings stay coherent without double-subscribing.
/// </summary>
public sealed partial class ToolModeService : ObservableObject, IToolModeService
{
    [ObservableProperty] private ToolMode    _tool              = ToolMode.Select;
    [ObservableProperty] private CreateType  _createType        = CreateType.Section;
    [ObservableProperty] private ToolMode?   _previewTool;
    [ObservableProperty] private CreateType? _previewCreateType;

    public ToolMode   EffectiveTool       => PreviewTool       ?? Tool;
    public CreateType EffectiveCreateType => PreviewCreateType ?? CreateType;

    // Re-raise the computed properties whenever their inputs change, so the
    // ContextBar's EffectiveTool-bound highlights update in the same tick
    // the underlying state moves. Without these hooks the generator-emitted
    // notifications fire only for Tool/PreviewTool themselves and the UI
    // lags by a frame.
    partial void OnToolChanged(ToolMode value)
        => OnPropertyChanged(nameof(EffectiveTool));

    partial void OnPreviewToolChanged(ToolMode? value)
        => OnPropertyChanged(nameof(EffectiveTool));

    partial void OnCreateTypeChanged(CreateType value)
        => OnPropertyChanged(nameof(EffectiveCreateType));

    partial void OnPreviewCreateTypeChanged(CreateType? value)
        => OnPropertyChanged(nameof(EffectiveCreateType));
}
