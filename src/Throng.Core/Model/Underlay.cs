using Throng.Core.Geometry;

namespace Throng.Core.Model;

/// <summary>
/// A calibrated plan image on a level (D-009, D-083–D-085). The image bytes are embedded in the .throng zip under
/// <see cref="ImageEntry"/> and held in memory in <see cref="Study.UnderlayImages"/>; this object only stores the
/// placement. Plan only: never drawn in 3D.
/// </summary>
public sealed class Underlay
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name (the original file name by default).</summary>
    public string Name { get; set; } = "Underlay";

    /// <summary>Level it's drawn on.</summary>
    public Guid LevelId { get; set; }

    /// <summary>Zip entry holding the image, e.g. "underlays/3f…a1.png".</summary>
    public string ImageEntry { get; set; } = string.Empty;

    /// <summary>Image width in pixels.</summary>
    public int PixelWidth { get; set; }

    /// <summary>Image height in pixels.</summary>
    public int PixelHeight { get; set; }

    /// <summary>Plan position of the image centre.</summary>
    public Point2 Centre { get; set; }

    /// <summary>Scale: plan metres per image pixel (set by calibration, D-084).</summary>
    public double MetresPerPixel { get; set; } = 0.05;

    /// <summary>Rotation in degrees, counter-clockwise.</summary>
    public double AngleDeg { get; set; }

    /// <summary>Opacity 0–1.</summary>
    public double Opacity { get; set; } = 0.6;

    /// <summary>Drawn when true.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Locked underlays can't be picked, moved or rotated, so you can draw over them.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Returns an independent copy, including the id.</summary>
    public Underlay Clone()
    {
        var copy = new Underlay();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>Copies every value (including the id) from <paramref name="other"/>.</summary>
    public void CopyFrom(Underlay other)
    {
        Id = other.Id;
        Name = other.Name;
        LevelId = other.LevelId;
        ImageEntry = other.ImageEntry;
        PixelWidth = other.PixelWidth;
        PixelHeight = other.PixelHeight;
        Centre = other.Centre;
        MetresPerPixel = other.MetresPerPixel;
        AngleDeg = other.AngleDeg;
        Opacity = other.Opacity;
        IsVisible = other.IsVisible;
        IsLocked = other.IsLocked;
    }
}
