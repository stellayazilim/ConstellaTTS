namespace ConstellaTTS.SDK.UI.Keybinds;

/// <summary>
/// An ordered set of <see cref="KeyMap"/> keys that together form a keyboard
/// shortcut (e.g. Ctrl+Shift+L).
/// Created via the | operator:
///   <code>KeyCombo binding = KeyMap.Ctrl | KeyMap.Shift | KeyMap.L;</code>
/// Matching is set-based — key order does not matter.
/// </summary>
public sealed class KeyCombo : IEquatable<KeyCombo>
{
    private readonly byte[] _bytes;
    public KeyMap[] Keys { get; }

    public KeyCombo(params KeyMap[] keys)
    {
        Keys   = keys.Distinct().OrderBy(k => k.ByteValue).ToArray();
        _bytes = Keys.Select(k => k.ByteValue).ToArray();
    }

    private KeyCombo(KeyMap[] sortedKeys, byte[] sortedBytes)
    {
        Keys   = sortedKeys;
        _bytes = sortedBytes;
    }

    public static KeyCombo operator |(KeyCombo combo, KeyMap key)
    {
        if (Array.IndexOf(combo._bytes, key.ByteValue) >= 0) return combo;
        var keys  = new KeyMap[combo.Keys.Length + 1];
        var bytes = new byte[combo._bytes.Length + 1];
        Array.Copy(combo.Keys,   keys,  combo.Keys.Length);
        Array.Copy(combo._bytes, bytes, combo._bytes.Length);
        keys[^1]  = key;
        bytes[^1] = key.ByteValue;
        var sorted = keys.OrderBy(k => k.ByteValue).ToArray();
        return new KeyCombo(sorted, sorted.Select(k => k.ByteValue).ToArray());
    }

    public bool Matches(HashSet<byte> pressedBytes)
    {
        if (pressedBytes.Count != _bytes.Length) return false;
        foreach (var b in _bytes)
            if (!pressedBytes.Contains(b)) return false;
        return true;
    }

    public byte[] ToBytes() => (byte[])_bytes.Clone();

    public static KeyCombo FromBytes(ReadOnlySpan<byte> bytes)
    {
        var maps = new KeyMap[bytes.Length];
        int count = 0;
        foreach (var b in bytes)
        {
            var km = KeyMap.FromByte(b);
            if (km is not null) maps[count++] = km;
        }
        return new KeyCombo(maps[..count]);
    }

    public static KeyCombo Parse(string gesture)
    {
        var tokens = gesture.Split('+',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var maps  = new KeyMap[tokens.Length];
        int count = 0;
        foreach (var token in tokens)
        {
            var km = KeyMap.FromDisplayName(token);
            if (km is not null) maps[count++] = km;
        }
        return new KeyCombo(maps[..count]);
    }

    public bool Equals(KeyCombo? other)
    {
        if (other is null || _bytes.Length != other._bytes.Length) return false;
        for (int i = 0; i < _bytes.Length; i++)
            if (_bytes[i] != other._bytes[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is KeyCombo other && Equals(other);
    public override int  GetHashCode()
    {
        var h = new HashCode();
        foreach (var b in _bytes) h.Add(b);
        return h.ToHashCode();
    }
    public static bool operator ==(KeyCombo? a, KeyCombo? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(KeyCombo? a, KeyCombo? b) => !(a == b);
    public override string ToString() => string.Join("+", Keys.Select(k => k.DisplayName));
}
