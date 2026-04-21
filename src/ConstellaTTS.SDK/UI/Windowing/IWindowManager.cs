using Avalonia.Controls;

namespace ConstellaTTS.SDK.UI.Windowing;

/// <summary>
/// Tracks and controls application windows — opening, closing, and recording the active window.
/// Does not create windows directly; delegates to IWindowFactory for platform-specific logic.
/// </summary>
public interface IWindowManager
{
    /// <summary>The type of the currently active window.</summary>
    Type ActiveWindowType { get; }

    /// <summary>All currently open window types.</summary>
    IReadOnlyList<Type> OpenWindows { get; }

    /// <summary>
    /// Returns the default (main) window instance.
    /// Executes any deferred mount actions on the first call.
    /// </summary>
    Window GetDefaultWindow();

    /// <summary>
    /// Defers a mount action until GetDefaultWindow is first called.
    /// Use this inside a module's Build() method when the window is not yet available.
    /// </summary>
    void DeferMount(Action<Window> mountAction);

    /// <summary>Opens a window of the specified type.</summary>
    void Open(Type windowType);

    /// <summary>Closes a window of the specified type.</summary>
    void Close(Type windowType);
}
