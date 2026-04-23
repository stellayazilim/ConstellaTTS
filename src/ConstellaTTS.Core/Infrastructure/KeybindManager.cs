using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConstellaTTS.Core.Logging;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.UI.Keybinds;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.Infrastructure;

public sealed class KeybindManager : IKeybindManager, IDisposable
{
    private readonly Dictionary<KeyCombo, IBindable> _map           = new();
    private readonly HashSet<byte>                   _pressed        = new();
    private readonly HashSet<Window>                 _trackedWindows = new();
    private readonly ILogger                         _log;

    private Window? _activeWindow;

    public event EventHandler<IAction>? ActionMatched;

    public KeybindManager(ILoggerFactory loggerFactory)
        => _log = loggerFactory.CreateLogger(LogCategory.WindowProcess);

    // ── Window tracking ───────────────────────────────────────────────────

    public void TrackWindow(Window window)
    {
        if (!_trackedWindows.Add(window)) return;

        window.Activated   += (_, _) => SwitchActive(window);
        window.Deactivated += (_, _) => OnWindowDeactivated();
        window.Closed      += (_, _) =>
        {
            _trackedWindows.Remove(window);
            if (_activeWindow == window)
            {
                DetachHandlers(window);
                _activeWindow = null;
            }
        };
    }

    private void OnWindowDeactivated()
    {
        // Post — Activated event'i Deactivated'dan sonra gelir,
        // pencereler arası geçişte temizleme yapmamak için bekle
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_trackedWindows.All(w => !w.IsActive))
            {
                _pressed.Clear();
                _log.LogDebug("KeybindManager: app lost focus — pressed set cleared");
            }
        });
    }

    private void SwitchActive(Window window)
    {
        if (_activeWindow == window) return;

        // Sadece handler'ları taşı — pressed set'e dokunma
        if (_activeWindow is not null)
            DetachHandlers(_activeWindow);

        _activeWindow = window;
        AttachHandlers(_activeWindow);

        _log.LogDebug("KeybindManager: active → {Type}", window.GetType().Name);
    }

    private void AttachHandlers(Window window)
    {
        window.AddHandler(
            InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        window.AddHandler(
            InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void DetachHandlers(Window window)
    {
        window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        window.RemoveHandler(InputElement.KeyUpEvent,   OnKeyUp);
        // _pressed.Clear() YOK — pencereler arası geçişte tuşlar korunur
    }

    // ── Key events ────────────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vk = ToVkByte(e.Key);
        if (vk is null) return;

        _pressed.Add(vk.Value);
        _log.LogInformation("KeyDown 0x{VK:X2} → pressed=[{Set}]",
            vk.Value, string.Join(", ", _pressed.Select(x => $"0x{x:X2}")));

        foreach (var (combo, action) in _map)
        {
            if (!combo.Matches(_pressed) || action is not IAction ia) continue;

            _log.LogInformation("Keybind MATCHED: {Id}", ia.Id);
            // Eşleşme sonrası temizleme YOK — tuşlar fiziksel olarak bırakılınca KeyUp gelir
            ActionMatched?.Invoke(this, ia);
            ia.Execute();
            return;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        var vk = ToVkByte(e.Key);
        if (vk is null) return;

        _pressed.Remove(vk.Value);
        _log.LogInformation("KeyUp  0x{VK:X2} → pressed=[{Set}]",
            vk.Value, string.Join(", ", _pressed.Select(x => $"0x{x:X2}")));
    }

    // ── IKeybindManager ───────────────────────────────────────────────────

    public void Register(IBindable action)
    {
        foreach (var combo in action.Bindings)
        {
            _map[combo] = action;
            _log.LogInformation("Keybind registered: {Combo} → {Id}",
                combo, (action as IAction)?.Id ?? "?");
        }
    }

    public void Unregister(IBindable action)
    {
        foreach (var combo in action.Bindings)
            _map.Remove(combo);
    }

    public void Rebind(IBindable action, KeyCombo[] newBindings)
    {
        Unregister(action);
        action.Bindings = newBindings;
        Register(action);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static byte? ToVkByte(Key key) => key switch
    {
        Key.LeftCtrl  or Key.RightCtrl  => 0x11,
        Key.LeftShift or Key.RightShift => 0x10,
        Key.LeftAlt   or Key.RightAlt   => 0x12,
        Key.LWin      or Key.RWin       => 0x5B,

        Key.A => 0x41, Key.B => 0x42, Key.C => 0x43, Key.D => 0x44,
        Key.E => 0x45, Key.F => 0x46, Key.G => 0x47, Key.H => 0x48,
        Key.I => 0x49, Key.J => 0x4A, Key.K => 0x4B, Key.L => 0x4C,
        Key.M => 0x4D, Key.N => 0x4E, Key.O => 0x4F, Key.P => 0x50,
        Key.Q => 0x51, Key.R => 0x52, Key.S => 0x53, Key.T => 0x54,
        Key.U => 0x55, Key.V => 0x56, Key.W => 0x57, Key.X => 0x58,
        Key.Y => 0x59, Key.Z => 0x5A,

        Key.D0 => 0x30, Key.D1 => 0x31, Key.D2 => 0x32, Key.D3 => 0x33,
        Key.D4 => 0x34, Key.D5 => 0x35, Key.D6 => 0x36, Key.D7 => 0x37,
        Key.D8 => 0x38, Key.D9 => 0x39,

        Key.F1  => 0x70, Key.F2  => 0x71, Key.F3  => 0x72, Key.F4  => 0x73,
        Key.F5  => 0x74, Key.F6  => 0x75, Key.F7  => 0x76, Key.F8  => 0x77,
        Key.F9  => 0x78, Key.F10 => 0x79, Key.F11 => 0x7A, Key.F12 => 0x7B,

        Key.Left  => 0x25, Key.Up    => 0x26, Key.Right => 0x27, Key.Down => 0x28,
        Key.Home  => 0x24, Key.End   => 0x23,
        Key.PageUp => 0x21, Key.PageDown => 0x22,
        Key.Insert => 0x2D, Key.Delete   => 0x2E,

        Key.Back   => 0x08, Key.Tab    => 0x09,
        Key.Return => 0x0D, Key.Escape => 0x1B,
        Key.Space  => 0x20,

        Key.NumPad0 => 0x60, Key.NumPad1 => 0x61, Key.NumPad2 => 0x62,
        Key.NumPad3 => 0x63, Key.NumPad4 => 0x64, Key.NumPad5 => 0x65,
        Key.NumPad6 => 0x66, Key.NumPad7 => 0x67, Key.NumPad8 => 0x68,
        Key.NumPad9 => 0x69,

        _ => null
    };

    public void Dispose()
    {
        if (_activeWindow is not null)
            DetachHandlers(_activeWindow);
        _activeWindow = null;
        _trackedWindows.Clear();
    }
}
