using System.Globalization;

namespace Throng.Render.Scene;

/// <summary>Linear RGBA colour with float channels in 0..1.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="A">Alpha.</param>
public readonly record struct ColorF(float R, float G, float B, float A = 1f)
{
    /// <summary>Parses "#RRGGBB" or "#AARRGGBB"; returns magenta for anything unparseable so mistakes are visible.</summary>
    public static ColorF FromHex(string hex, float alpha = 1f)
    {
        string h = hex.TrimStart('#');
        if (h.Length == 8 && uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            return new ColorF(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, ((argb >> 24) & 0xFF) / 255f * alpha);
        }

        if (h.Length == 6 && uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return new ColorF(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
        }

        return new ColorF(1f, 0f, 1f, alpha);
    }

    /// <summary>Same colour with a different alpha.</summary>
    public ColorF WithAlpha(float alpha) => this with { A = alpha };

    /// <summary>Colour scaled towards black by <paramref name="factor"/> (1 = unchanged).</summary>
    public ColorF Shade(float factor) => new(R * factor, G * factor, B * factor, A);
}
