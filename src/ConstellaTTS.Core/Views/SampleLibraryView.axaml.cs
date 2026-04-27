using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.SDK.Audio;

namespace ConstellaTTS.Core.Views;

/// <summary>
/// Sample library panel. Adds drag-source behaviour on top of the
/// XAML-defined item list: when the user presses on a sample row,
/// a drag operation starts immediately with the sample's identifier
/// as payload. The drop side lives in <c>TrackListView</c>, which
/// accepts only <see cref="ISectionViewModel"/> targets and
/// dispatches <c>AssignSampleAction</c>.
///
/// <para>
/// <b>Why no click-vs-drag threshold.</b> Avalonia 12's
/// <c>DragDrop.DoDragDropAsync</c> requires a
/// <see cref="PointerPressedEventArgs"/> trigger, so the call must
/// happen inside the press handler itself — there's no API path to
/// promote a later <see cref="PointerMovedEventArgs"/> to a drag
/// trigger. We start the drag on press; if the user releases
/// without moving past the platform's drag threshold, Avalonia
/// cancels the operation and DoDragDropAsync returns
/// <see cref="DragDropEffects.None"/> — effectively a click. Sample
/// items don't have any click-only behaviour to preserve, so this
/// "press = drag start" model loses nothing.
/// </para>
///
/// <para>
/// <b>Why an application format.</b> The drag payload is a string
/// but it travels through <see cref="SampleDragFormat"/> (an
/// application-scoped <see cref="DataFormat{T}"/>) rather than the
/// generic <see cref="DataFormat.Text"/> — that way the drop target
/// can reject random text drags from outside the app (browsers love
/// to drop URLs as plain text), and the source's intent stays
/// explicit. The string itself is the sample's <c>OriginalName</c>;
/// the section's <c>VoiceSampleRef</c> contract today expects a
/// filename, so the payload format matches the destination shape
/// and no translation is needed at drop time. Identifier characters
/// are restricted by Avalonia to ASCII letters/digits/dot/dash, so
/// the historical <c>constellatts/sample-ref</c> form was illegal —
/// the dash form below is the closest legal equivalent.
/// </para>
/// </summary>
public partial class SampleLibraryView : UserControl
{
    /// <summary>
    /// Application-format key for sample-library payloads. Drop
    /// targets retrieve the sample reference via this exact format;
    /// payloads from outside the app (browser URL drags, file
    /// drops) carry different formats and are silently ignored.
    /// Static so the drop side can match against the same instance.
    /// </summary>
    public static readonly DataFormat<string> SampleDragFormat =
        DataFormat.CreateStringApplicationFormat("constellatts-sample-ref");

    public SampleLibraryView(SampleLibraryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Class-level pointer hook rather than per-item handlers in
        // XAML — the items collection is data-bound and grows /
        // shrinks at runtime, so attaching from the parent UserControl
        // lets us hit-test up the visual tree from the actual press
        // location instead of wiring DataTemplate-level events that
        // would have to be re-registered on every collection change.
        AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: false);
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Find the Sample DataContext under the press point. The
        // DataTemplate is a Border holding text — walking the
        // visual tree from the source until we hit a Control whose
        // DataContext is a Sample is the cleanest way to identify
        // which row the user grabbed without the items control
        // exposing per-item Tag plumbing.
        if (e.Source is not Visual visual) return;

        var sample = visual.GetSelfAndVisualAncestors()
                           .OfType<Control>()
                           .Select(c => c.DataContext as Sample)
                           .FirstOrDefault(s => s is not null);
        if (sample is null) return;

        // Avalonia 12 DataTransfer model: build a single item
        // carrying the format → value pair, wrap it in a transfer
        // container, hand it to DoDragDropAsync. The system takes
        // ownership of the IDataTransfer and disposes it once the
        // drag operation completes — caller MUST NOT dispose it.
        var item = new DataTransferItem();
        item.Set(SampleDragFormat, sample.OriginalName);

        var data = new DataTransfer();
        data.Add(item);

        // DragDropEffects.Link is the closest semantic match — the
        // section will reference the sample by name, the sample
        // file itself doesn't move or get copied. The drop side
        // explicitly requests Link too so the cursor matches.
        // Return value is ignored — there's nothing to undo on the
        // source side regardless of whether the user dropped on a
        // valid target or cancelled mid-air.
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Link);
    }
}
