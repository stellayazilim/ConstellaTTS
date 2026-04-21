using Avalonia.Controls;
using ConstellaTTS.SDK.UI.Slots;
using ConstellaTTS.SDK.UI.Windowing;
using ConstellaTTS.Core.Layout;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Core.UI.Infrastructure;

/// <summary>
/// Avalonia implementation of IWindowFactory.
/// Resolves window instances from the DI container and mounts the default slot map
/// on first access to the main window.
/// </summary>
public sealed class AvaloniaWindowFactory(
    IServiceProvider services,
    ISlotService slotService) : IWindowFactory
{
    private readonly Dictionary<Type, Window> _instances = new();
    private Type? _defaultWindowType;
    private bool  _defaultMounted;

    /// <inheritdoc/>
    public void SetDefaultWindow(Type windowType) =>
        _defaultWindowType = windowType;

    /// <inheritdoc/>
    public Window GetDefaultWindow()
    {
        if (_defaultWindowType is null)
            throw new InvalidOperationException("Default window type is not set.");

        var window = GetOrCreate(_defaultWindowType);

        if (!_defaultMounted)
        {
            _defaultMounted = true;

            slotService.Mount(
                _defaultWindowType,
                Slots.Content,
                typeof(MainLayout),
                childSlots: new SlotMap()
                    .Add(Slots.Toolbar,   SlotType.Control)
                    .Add(Slots.ViewTools, SlotType.Control));
        }

        return window;
    }

    /// <inheritdoc/>
    public Window GetWindow(Type windowType) => GetOrCreate(windowType);

    /// <inheritdoc/>
    public void Show(Type windowType) => GetOrCreate(windowType).Show();

    /// <inheritdoc/>
    public void Close(Type windowType)
    {
        if (_instances.TryGetValue(windowType, out var window))
        {
            window.Close();
            _instances.Remove(windowType);
        }
    }

    /// <inheritdoc/>
    public bool IsOpen(Type windowType) => _instances.ContainsKey(windowType);

    private Window GetOrCreate(Type windowType)
    {
        if (_instances.TryGetValue(windowType, out var existing))
            return existing;

        var window = (Window)services.GetRequiredService(windowType);
        _instances[windowType] = window;
        return window;
    }
}
