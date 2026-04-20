using Avalonia.Controls;

namespace ConstellaTTS.SDK;

/// <summary>
/// Manages application windows. Opening and closing is delegated to
/// <see cref="IWindowFactory"/> — SDK has no UI dependency.
/// </summary>
public sealed class WindowManager(IWindowFactory factory) : IWindowManager
{
    private readonly List<Type>            _openWindows   = [];
    private readonly HashSet<Type>         _registered    = [];
    private readonly List<Action<Window>>  _deferredMounts = [];
    private Type?  _activeWindowType;
    private bool   _defaultResolved = false;

    /// <inheritdoc/>
    public Type ActiveWindowType =>
        _activeWindowType ?? throw new InvalidOperationException("No active window.");

    /// <inheritdoc/>
    public IReadOnlyList<Type> OpenWindows => _openWindows;

    /// <inheritdoc/>
    public Window GetDefaultWindow()
    {
        var window = factory.GetDefaultWindow();

        // Execute deferred mounts once
        if (!_defaultResolved)
        {
            foreach (var action in _deferredMounts)
                action(window);

            _deferredMounts.Clear();
            _defaultResolved = true;
        }

        return window;
    }

    /// <inheritdoc/>
    public void DeferMount(Action<Window> mountAction) => _deferredMounts.Add(mountAction);

    /// <summary>Registers a window type. Called by modules in their Build() method.</summary>
    public void RegisterWindow(Type windowType) => _registered.Add(windowType);

    /// <inheritdoc/>
    public void Open(Type windowType)
    {
        EnsureRegistered(windowType);
        factory.Show(windowType);
        _openWindows.Add(windowType);
        _activeWindowType = windowType;
    }

    /// <inheritdoc/>
    public void Close(Type windowType)
    {
        EnsureRegistered(windowType);
        factory.Close(windowType);
        _openWindows.Remove(windowType);

        if (_activeWindowType == windowType)
            _activeWindowType = _openWindows.LastOrDefault();
    }

    /// <summary>
    /// Marks a window as active without opening it —
    /// used at startup when the platform opens the main window directly.
    /// </summary>
    public void SetActive(Type windowType)
    {
        if (!_openWindows.Contains(windowType))
            _openWindows.Add(windowType);
        _activeWindowType = windowType;
    }

    private void EnsureRegistered(Type windowType)
    {
        if (!_registered.Contains(windowType))
            throw new InvalidOperationException(
                $"Window '{windowType.Name}' is not registered. " +
                $"Call RegisterWindow() in your module's Build() method.");
    }
}
