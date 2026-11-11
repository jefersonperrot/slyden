using System.Globalization;

namespace Slyden.Core.Config;

/// <summary>Cor em texto: "#RRGGBB" ou "#RRGGBBAA" (o último par é a opacidade).</summary>
public static class HexColor
{
    public static bool TryParse(string? text, out (byte R, byte G, byte B, byte A) color)
    {
        color = default;
        if (text is null) return false;
        var s = text.Trim();
        if (!s.StartsWith('#') || (s.Length != 7 && s.Length != 9)) return false;

        Span<byte> v = stackalloc byte[4];
        v[3] = 255;
        for (int i = 0; i < (s.Length - 1) / 2; i++)
            if (!byte.TryParse(s.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v[i]))
                return false;

        color = (v[0], v[1], v[2], v[3]);
        return true;
    }
}
