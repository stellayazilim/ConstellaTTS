namespace ConstellaTTS.SDK.UI.Keybinds;

/// <summary>
/// Smart enum — each static instance represents exactly one key.
/// Combine with | to produce a <see cref="KeyCombo"/>.
/// </summary>
public sealed class KeyMap : IEquatable<KeyMap>
{
    public static readonly KeyMap Ctrl  = new(0x11, "Ctrl");
    public static readonly KeyMap Shift = new(0x10, "Shift");
    public static readonly KeyMap Alt   = new(0x12, "Alt");
    public static readonly KeyMap Meta  = new(0x5B, "Meta");

    public static readonly KeyMap A = new(0x41, "A"); public static readonly KeyMap B = new(0x42, "B");
    public static readonly KeyMap C = new(0x43, "C"); public static readonly KeyMap D = new(0x44, "D");
    public static readonly KeyMap E = new(0x45, "E"); public static readonly KeyMap F = new(0x46, "F");
    public static readonly KeyMap G = new(0x47, "G"); public static readonly KeyMap H = new(0x48, "H");
    public static readonly KeyMap I = new(0x49, "I"); public static readonly KeyMap J = new(0x4A, "J");
    public static readonly KeyMap K = new(0x4B, "K"); public static readonly KeyMap L = new(0x4C, "L");
    public static readonly KeyMap M = new(0x4D, "M"); public static readonly KeyMap N = new(0x4E, "N");
    public static readonly KeyMap O = new(0x4F, "O"); public static readonly KeyMap P = new(0x50, "P");
    public static readonly KeyMap Q = new(0x51, "Q"); public static readonly KeyMap R = new(0x52, "R");
    public static readonly KeyMap S = new(0x53, "S"); public static readonly KeyMap T = new(0x54, "T");
    public static readonly KeyMap U = new(0x55, "U"); public static readonly KeyMap V = new(0x56, "V");
    public static readonly KeyMap W = new(0x57, "W"); public static readonly KeyMap X = new(0x58, "X");
    public static readonly KeyMap Y = new(0x59, "Y"); public static readonly KeyMap Z = new(0x5A, "Z");

    public static readonly KeyMap D0 = new(0x30, "0"); public static readonly KeyMap D1 = new(0x31, "1");
    public static readonly KeyMap D2 = new(0x32, "2"); public static readonly KeyMap D3 = new(0x33, "3");
    public static readonly KeyMap D4 = new(0x34, "4"); public static readonly KeyMap D5 = new(0x35, "5");
    public static readonly KeyMap D6 = new(0x36, "6"); public static readonly KeyMap D7 = new(0x37, "7");
    public static readonly KeyMap D8 = new(0x38, "8"); public static readonly KeyMap D9 = new(0x39, "9");

    public static readonly KeyMap F1  = new(0x70, "F1");  public static readonly KeyMap F2  = new(0x71, "F2");
    public static readonly KeyMap F3  = new(0x72, "F3");  public static readonly KeyMap F4  = new(0x73, "F4");
    public static readonly KeyMap F5  = new(0x74, "F5");  public static readonly KeyMap F6  = new(0x75, "F6");
    public static readonly KeyMap F7  = new(0x76, "F7");  public static readonly KeyMap F8  = new(0x77, "F8");
    public static readonly KeyMap F9  = new(0x78, "F9");  public static readonly KeyMap F10 = new(0x79, "F10");
    public static readonly KeyMap F11 = new(0x7A, "F11"); public static readonly KeyMap F12 = new(0x7B, "F12");

    public static readonly KeyMap Left     = new(0x25, "Left");   public static readonly KeyMap Up       = new(0x26, "Up");
    public static readonly KeyMap Right    = new(0x27, "Right");  public static readonly KeyMap Down     = new(0x28, "Down");
    public static readonly KeyMap Home     = new(0x24, "Home");   public static readonly KeyMap End      = new(0x23, "End");
    public static readonly KeyMap PageUp   = new(0x21, "PageUp"); public static readonly KeyMap PageDown = new(0x22, "PageDown");
    public static readonly KeyMap Insert   = new(0x2D, "Insert"); public static readonly KeyMap Delete   = new(0x2E, "Delete");

    public static readonly KeyMap Backspace = new(0x08, "Backspace");
    public static readonly KeyMap Tab       = new(0x09, "Tab");
    public static readonly KeyMap Enter     = new(0x0D, "Enter");
    public static readonly KeyMap Escape    = new(0x1B, "Escape");
    public static readonly KeyMap Space     = new(0x20, "Space");

    public static readonly KeyMap NumPad0 = new(0x60, "NumPad0"); public static readonly KeyMap NumPad1 = new(0x61, "NumPad1");
    public static readonly KeyMap NumPad2 = new(0x62, "NumPad2"); public static readonly KeyMap NumPad3 = new(0x63, "NumPad3");
    public static readonly KeyMap NumPad4 = new(0x64, "NumPad4"); public static readonly KeyMap NumPad5 = new(0x65, "NumPad5");
    public static readonly KeyMap NumPad6 = new(0x66, "NumPad6"); public static readonly KeyMap NumPad7 = new(0x67, "NumPad7");
    public static readonly KeyMap NumPad8 = new(0x68, "NumPad8"); public static readonly KeyMap NumPad9 = new(0x69, "NumPad9");

    public byte   ByteValue   { get; }
    public string DisplayName { get; }

    private KeyMap(byte byteValue, string displayName)
    {
        ByteValue   = byteValue;
        DisplayName = displayName;
    }

    public static KeyCombo operator |(KeyMap a, KeyMap b) => new(a, b);

    public bool Equals(KeyMap? other)        => other is not null && ByteValue == other.ByteValue;
    public override bool Equals(object? obj) => obj is KeyMap other && Equals(other);
    public override int  GetHashCode()       => ByteValue;
    public static bool operator ==(KeyMap? a, KeyMap? b) => a?.ByteValue == b?.ByteValue;
    public static bool operator !=(KeyMap? a, KeyMap? b) => !(a == b);
    public override string ToString() => DisplayName;

    public static KeyMap? FromByte(byte value)          => _byteRegistry.TryGetValue(value, out var km) ? km : null;
    public static KeyMap? FromDisplayName(string name)  => _nameRegistry.TryGetValue(name,  out var km) ? km : null;

    private static readonly Dictionary<byte,   KeyMap> _byteRegistry;
    private static readonly Dictionary<string, KeyMap> _nameRegistry;

    static KeyMap()
    {
        var fields = typeof(KeyMap)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(KeyMap));

        _byteRegistry = new(128);
        _nameRegistry = new(128, StringComparer.OrdinalIgnoreCase);

        foreach (var f in fields)
        {
            if (f.GetValue(null) is not KeyMap km) continue;
            _byteRegistry[km.ByteValue]   = km;
            _nameRegistry[km.DisplayName] = km;
        }
    }
}
