using Avalonia.Controls;
using ConstellaTTS.Core.Layout;
using ConstellaTTS.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// Avalonia implementation of <see cref="IWindowFactory"/>.
/// Resolves window instances via DI, mounts default slot map on first access.
/// </summary>
public sealed class AvaloniaWindowFactory(
    IServiceProvider services,
    ISlotService slotService) : IWindowFactory
{
    private readonly Dictionary<Type, Window> _instances = new();
    private Type? _defaultWindowType;
    private bool _defaultMounted = false;

    /// <inheritdoc/>
    public void SetDefaultWindow(Type windowType) => _defaultWindowType = windowType;

    /// <inheritdoc/>
    public Window GetDefaultWindow()
    {
        if (_defaultWindowType is null)
            throw new InvalidOperationException("Default window type is not set.");

        var window = GetOrCreate(_defaultWindowType);

        // Mount default slot map once
        if (!_defaultMounted)
        {
            MountDefaultSlots(_defaultWindowType);
            _defaultMounted = true;
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

    private void MountDefaultSlots(Type windowType)
    {
        // Mount MainLayout into Content slot — exposes Toolbar + ViewTools child slots
        slotService.Mount(
            windowType,
            Slots.Content,
            typeof(MainLayout),
            childSlots: new SlotMap()
                .Add(Slots.Toolbar,   SlotType.Control)
                .Add(Slots.ViewTools, SlotType.Control));

        // Resolve and attach layout to window
        if (_instances.TryGetValue(windowType, out var window) &&
            window.FindControl<ContentControl>("LayoutSlot") is { } layoutSlot)
        {
            layoutSlot.Content = services.GetRequiredService<MainLayout>();
        }
    }

    private Window GetOrCreate(Type windowType)
    {
        if (_instances.TryGetValue(windowType, out var existing))
            return existing;

        var window = (Window)services.GetRequiredService(windowType);
        _instances[windowType] = window;
        return window;
    }
}
