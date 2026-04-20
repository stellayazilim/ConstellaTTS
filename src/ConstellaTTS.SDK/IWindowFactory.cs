using Avalonia.Controls;

namespace ConstellaTTS.SDK;

/// <summary>
/// Abstracts platform-specific window creation and lifecycle.
/// Implemented by the UI layer (e.g. Avalonia) — SDK has no UI dependency.
/// </summary>
public interface IWindowFactory
{
    /// <summary>Creates and shows a window of the specified type.</summary>
    void Show(Type windowType);

    /// <summary>Closes a window of the specified type.</summary>
    void Close(Type windowType);

    /// <summary>Returns true if a window of this type is currently open.</summary>
    bool IsOpen(Type windowType);

    /// <summary>
    /// Returns the default (main) window instance.
    /// Default window type is set via <see cref="SetDefaultWindow"/>.
    /// </summary>
    Window GetDefaultWindow();

    /// <summary>
    /// Returns a window instance of the specified type.
    /// </summary>
    Window GetWindow(Type windowType);

    /// <summary>
    /// Sets the default window type — called by Core at startup.
    /// </summary>
    void SetDefaultWindow(Type windowType);
}
