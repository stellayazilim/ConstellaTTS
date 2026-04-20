using Avalonia.Controls;

namespace ConstellaTTS.SDK;

/// <summary>
/// Manages application windows — opening, closing, and tracking active window.
/// </summary>
public interface IWindowManager
{
    /// <summary>Currently active window type.</summary>
    Type ActiveWindowType { get; }

    /// <summary>All currently open window types.</summary>
    IReadOnlyList<Type> OpenWindows { get; }

    /// <summary>
    /// Returns the default window instance — type is set by Core at startup.
    /// All deferred mount actions are executed on first call.
    /// </summary>
    Window GetDefaultWindow();

    /// <summary>
    /// Defers a mount action until <see cref="GetDefaultWindow"/> is called.
    /// Use this when registering mounts before the window is ready —
    /// e.g. inside a module's Build() method.
    /// </summary>
    void DeferMount(Action<Window> mountAction);

    /// <summary>Opens a window of the specified type.</summary>
    void Open(Type windowType);

    /// <summary>Closes a window of the specified type.</summary>
    void Close(Type windowType);
}
