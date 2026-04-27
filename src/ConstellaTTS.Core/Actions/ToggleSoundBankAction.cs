using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.UI.Keybinds;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Tools;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Toggle for the Sample tool. Activating sets
/// <see cref="IToolModeService.Tool"/> to <see cref="ToolMode.Sample"/>
/// and shows the sample library window in lockstep; deactivating
/// hides the window and restores the tool the user was on before
/// the toggle.
///
/// <para>
/// <b>Tool and window are 1:1.</b> The window's visibility piggybacks
/// on the tool transition; the user only ever toggles the tool, not
/// the window directly. This keeps the "sample library is open"
/// condition observable through the same property-changed stream
/// the rest of the canvas already listens to (see
/// <see cref="ToolMode.Sample"/> remarks).
/// </para>
///
/// <para>
/// <b>Previous-tool restore.</b> Activating snapshots the tool the
/// user is currently on; deactivating writes that snapshot back.
/// This is what makes the toggle feel non-disruptive — opening the
/// sample library and closing it again leaves the user exactly
/// where they started, even if they were mid-flow in Create mode
/// drawing blocks. Without the snapshot the deactivation would
/// have to pick a default (Select), which would silently abandon
/// whatever tool the user was using.
/// </para>
///
/// <para>
/// <b>External tool changes deactivate.</b> If the user clicks
/// Select / Create on the context bar while the Sample tool is
/// active, the property-changed handler below sees Tool drift away
/// from <see cref="ToolMode.Sample"/> and hides the window in
/// response. The snapshot is dropped at that point — the user has
/// chosen a new tool deliberately, so a future re-activation of
/// the Sample tool starts a fresh snapshot from wherever they're
/// at then.
/// </para>
///
/// ActionBase + IBindable — Ctrl+L hotkey.
/// </summary>
public sealed class ToggleSoundBankAction : ActionBase, IBindable
{
    private readonly SampleLibraryWindow _window;
    private readonly INavigationManager  _navigation;
    private readonly IToolModeService    _tools;

    /// <summary>
    /// Tool the user was on when the Sample tool was activated.
    /// Restored on deactivation. Null when the Sample tool isn't
    /// active (or was activated from a state we somehow didn't
    /// snapshot, in which case deactivation falls back to
    /// <see cref="ToolMode.Select"/>).
    /// </summary>
    private ToolMode? _previousTool;

    public override string  Id          => "ToggleSoundBank";
    public override string  Name        => "Ses Bankası";
    public override string? Description => "Ses bankası panelini ve sample atama tool'unu açar veya kapatır.";
    public KeyCombo[]       Bindings    { get; set; } = [KeyMap.Ctrl | KeyMap.L];

    public bool IsWindowVisible => _window.IsVisible;

    public ToggleSoundBankAction(
        SampleLibraryWindow window,
        INavigationManager  navigation,
        IToolModeService    tools)
    {
        _window     = window;
        _navigation = navigation;
        _tools      = tools;

        _window.VisibilityChanged += (_, _) => RaiseCanExecuteChanged();

        // Watch for the user picking a different tool while the
        // Sample tool is active (e.g. clicking Select on the context
        // bar). When that happens, hide the window so the UI state
        // stays consistent — sample window visible only while the
        // Sample tool is the active tool.
        _tools.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(IToolModeService.Tool)) return;
            if (_tools.Tool == ToolMode.Sample) return;
            if (!_window.IsVisible) return;

            // The user moved off the Sample tool from outside this
            // action. Drop the snapshot (the user has chosen the
            // new tool deliberately) and close the window.
            _previousTool = null;
            _navigation.Navigate(new HideFlyoutRequest(typeof(SampleLibraryWindow)));
        };
    }

    public override void Execute(object? data = null)
    {
        if (_tools.Tool == ToolMode.Sample)
        {
            // Deactivate. Restore the previous tool first so the
            // PropertyChanged handler above doesn't fire its own
            // close-the-window branch (Tool change to non-Sample
            // would otherwise issue a redundant HideFlyoutRequest).
            // Setting Tool back to its prior value before hiding
            // the window puts both pieces of state into agreement
            // in the right order.
            var restore = _previousTool ?? ToolMode.Select;
            _previousTool = null;
            _tools.Tool   = restore;

            _navigation.Navigate(new HideFlyoutRequest(typeof(SampleLibraryWindow)));
        }
        else
        {
            // Activate. Snapshot the current tool so the matching
            // deactivate can restore it, then flip to Sample and
            // surface the window. The property-changed handler
            // skips this transition because Tool is becoming
            // Sample — its branch only triggers on the inverse
            // direction.
            _previousTool = _tools.Tool;
            _tools.Tool   = ToolMode.Sample;

            _navigation.Navigate(new ShowFlyoutRequest(typeof(SampleLibraryWindow)));
        }
    }
}
