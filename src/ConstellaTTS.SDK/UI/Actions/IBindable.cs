using ConstellaTTS.SDK.UI.Keybinds;

namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Opt-in interface for actions that can be bound to keyboard shortcuts.
/// IKeybindManager only accepts IBindable — NavigationRequest and other
/// non-hotkey actions never need to know about KeyCombo.
/// </summary>
public interface IBindable
{
    KeyCombo[] Bindings { get; set; }
}
