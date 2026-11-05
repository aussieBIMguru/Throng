namespace Throng.Render.Gl;

/// <summary>What agent bodies are coloured by (D-146).</summary>
public enum AgentColoring
{
    /// <summary>The profile's colour.</summary>
    Profile,

    /// <summary>Stress from cool (calm) to hot (fed up), D-145.</summary>
    Stress,

    /// <summary>
    /// Each group its own colour, people on their own grey (round 024, D-391). Drawn with the profile shader path: the
    /// app writes <see cref="GroupColours"/> into the snapshot's colour before upload.
    /// </summary>
    Group,
}

/// <summary>Group colours for <see cref="AgentColoring.Group"/> (D-391): a hue per group, grey for people on their own.</summary>
public static class GroupColours
{
    /// <summary>Grey for people on their own.</summary>
    public const float Single = 0.42f;

    /// <summary>
    /// The hue (1–255) of a group tag (the snapshot's float 11, round 024b; 0 = on their own → 0): golden-ratio steps
    /// round the colour wheel, so groups that spawn one after another look different.
    /// </summary>
    public static float HueOfTag(float tag)
    {
        if (!(tag >= 0.5f))
        {
            return 0;
        }

        double turn = (Math.Round(tag) * 0.6180339887498949) % 1.0;
        return 1 + (int)(turn * 254);
    }

    /// <summary>The colour for a group tag (snapshot float 11; 0 = on their own) as red, green, blue in 0–1.</summary>
    public static (float R, float G, float B) OfTag(float tag) => Of(HueOfTag(tag));

    /// <summary>The colour for a group hue (1–255; 0 = on their own) as red, green, blue in 0–1.</summary>
    public static (float R, float G, float B) Of(float hue)
    {
        if (!(hue >= 0.5f))
        {
            return (Single, Single, Single);
        }

        // HSV with fixed saturation and value: bright, easy to tell apart on the dark floor.
        double h = Math.Clamp((hue - 1) / 254.0, 0, 1) * 6.0;
        const double s = 0.65, v = 0.95;
        int sector = (int)Math.Floor(h) % 6;
        double f = h - Math.Floor(h);
        double p = v * (1 - s), q = v * (1 - (s * f)), t = v * (1 - (s * (1 - f)));
        (double r, double g, double b) = sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return ((float)r, (float)g, (float)b);
    }
}
