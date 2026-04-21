using Avalonia.Controls;

namespace ConstellaTTS.SDK.UI.Windowing;

/// <summary>
/// Abstracts platform-specific window creation and lifecycle management.
/// Implemented by the UI layer so the SDK stays free of Avalonia dependencies
/// where possible, while this interface bridges the gap.
/// </summary>
public interface IWindowFactory
{
    /// <summary>Creates and shows a window of the specified type.</summary>
    void Show(Type windowType);

    /// <summary>Closes a window of the specified type.</summary>
    void Close(Type windowType);

    /// <summary>Returns true if a window of the specified type is currently open.</summary>
    bool IsOpen(Type windowType);

    /// <summary>
    /// Returns the default (main) window instance.
    /// The default type is set via SetDefaultWindow.
    /// </summary>
    Window GetDefaultWindow();

    /// <summary>Returns a window instance of the specified type.</summary>
    Window GetWindow(Type windowType);

    /// <summary>Sets the window type that GetDefaultWindow returns. Called by Core at startup.</summary>
    void SetDefaultWindow(Type windowType);
}
