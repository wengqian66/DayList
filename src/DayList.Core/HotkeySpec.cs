namespace DayList.Core;

public readonly record struct HotkeySpec(bool Ctrl, bool Shift, bool Alt, int VirtualKey)
{
    public static HotkeySpec Default => Parse("Ctrl+Shift+D");

    public static HotkeySpec Parse(string? text)
    {
        var ctrl = false;
        var shift = false;
        var alt = false;
        int? key = null;

        foreach (var part in (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
                continue;
            }

            if (part.Length == 1)
            {
                var ch = char.ToUpperInvariant(part[0]);
                if (ch is >= 'A' and <= 'Z')
                {
                    key = ch;
                    continue;
                }

                if (ch is >= '0' and <= '9')
                {
                    key = ch;
                    continue;
                }
            }

            if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part[1..], out var fn)
                && fn is >= 1 and <= 24)
            {
                key = 0x70 + (fn - 1);
            }
        }

        if (key is null)
            return Default;

        return new HotkeySpec(ctrl, shift, alt, key.Value);
    }

    public string ToDisplay()
    {
        var parts = new List<string>();
        if (Ctrl)
            parts.Add("Ctrl");
        if (Shift)
            parts.Add("Shift");
        if (Alt)
            parts.Add("Alt");

        if (VirtualKey is >= 0x70 and <= 0x87)
            parts.Add("F" + (VirtualKey - 0x70 + 1));
        else if (VirtualKey is >= 'A' and <= 'Z' or >= '0' and <= '9')
            parts.Add(((char)VirtualKey).ToString());
        else
            parts.Add("D");

        return string.Join("+", parts);
    }

    public uint Modifiers
    {
        get
        {
            uint mods = 0;
            if (Alt)
                mods |= 0x0001;
            if (Ctrl)
                mods |= 0x0002;
            if (Shift)
                mods |= 0x0004;
            mods |= 0x4000; // MOD_NOREPEAT
            return mods;
        }
    }
}
