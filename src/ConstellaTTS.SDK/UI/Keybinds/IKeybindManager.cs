using Avalonia.Controls;
using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.SDK.UI.Keybinds;

/// <summary>
/// Maps KeyCombo → IBindable. Singleton — tracks all windows,
/// only attaches key handlers to the active (focused) window.
/// No double-fire possible.
/// </summary>
public interface IKeybindManager
{
    /// <summary>
    /// Register a window for tracking. KeybindManager attaches/detaches
    /// key handlers automatically as windows gain/lose focus.
    /// </summary>
    void TrackWindow(Window window);

    void Register(IBindable action);
    void Unregister(IBindable action);
    void Rebind(IBindable action, KeyCombo[] newBindings);

    /// <summary>Raised when a registered combo is fully pressed.</summary>
    event EventHandler<IAction> ActionMatched;
}
