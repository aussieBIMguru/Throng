using System.Globalization;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Core.Editing;

/// <summary>Creates new and copied regions with sensible defaults and readable names.</summary>
public static class RegionFactory
{
    /// <summary>Default extrusion height for new walls, in metres.</summary>
    public const double DefaultWallHeightM = 3.0;

    /// <summary>Default thickness for new walls, in metres.</summary>
    public const double DefaultWallThicknessM = 0.2;

    /// <summary>Name prefix used for new regions of a kind.</summary>
    public static string NamePrefix(RegionKind kind) => kind switch
    {
        RegionKind.Walkable => "Floor",
        RegionKind.Obstacle => "Obstacle",
        RegionKind.Wall => "Wall",
        RegionKind.Zone => "Zone",
        _ => "Region",
    };

    /// <summary>Creates a new region (not yet added to the study) with a unique "Prefix N" name.</summary>
    public static Region Create(Study study, Guid levelId, RegionKind kind, IEnumerable<Point2> outline)
    {
        var region = new Region
        {
            LevelId = levelId,
            Kind = kind,
            Name = NextName(study, NamePrefix(kind)),
            Outline = [.. outline],
        };

        if (kind == RegionKind.Wall)
        {
            region.HeightM = DefaultWallHeightM;
            region.ThicknessM = DefaultWallThicknessM;
        }

        return region;
    }

    /// <summary>
    /// Copy of <paramref name="source"/> with a new id, placed on <paramref name="levelId"/> and moved by
    /// <paramref name="offset"/>. Copies keep the source name.
    /// </summary>
    public static Region Copy(Region source, Guid levelId, Point2 offset) => new()
    {
        LevelId = levelId,
        Name = source.Name,
        Kind = source.Kind,
        ZoneKind = source.ZoneKind,
        Outline = ShapeFactory.Translate(source.Outline, offset),
        HeightM = source.HeightM,
        ThicknessM = source.ThicknessM,
        SpeedFactor = source.SpeedFactor,
        Tag = source.Tag,
    };

    /// <summary>Next free "Prefix N" name, one above the highest N already used in the study.</summary>
    public static string NextName(Study study, string prefix)
    {
        int highest = 0;
        string start = prefix + " ";
        foreach (Region region in study.Regions)
        {
            if (region.Name.StartsWith(start, StringComparison.Ordinal)
                && int.TryParse(region.Name.AsSpan(start.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                && n > highest)
            {
                highest = n;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{prefix} {highest + 1}");
    }
}
