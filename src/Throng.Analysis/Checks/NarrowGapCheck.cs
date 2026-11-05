using System.Globalization;
using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Core.World;

namespace Throng.Analysis.Checks;

/// <summary>
/// Walkable gaps narrower than the widest profile (round 016, D-274): each level's walkable area is shrunk by the
/// widest body radius and grown back (a morphological opening, mitred so square corners come back whole); whatever
/// doesn't come back is too narrow for that person to pass. Each such piece of any size is one warning, placed at it.
/// </summary>
public static class NarrowGapCheck
{
    /// <summary>Pieces smaller than this are rounding slivers, m².</summary>
    public const double MinPieceM2 = 0.02;

    /// <summary>Most warnings per level, so a badly drawn level doesn't flood CHECK.</summary>
    public const int MaxPerLevel = 8;

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>The narrow gap warnings for every level, for people of <paramref name="radiusM"/>.</summary>
    public static IReadOnlyList<ValidationMessage> Run(Study study, IPolygonOps ops, double radiusM)
    {
        var findings = new List<ValidationMessage>();
        if (radiusM <= 0)
        {
            return findings;
        }

        foreach (Level level in study.LevelsByElevation())
        {
            Result<IReadOnlyList<IReadOnlyList<Point2>>> area = WalkableArea.Compute(study, level.Id, ops);
            if (!area.IsSuccess || area.Value is not { Count: > 0 } walkable)
            {
                continue;
            }

            int count = 0;
            foreach (IReadOnlyList<Point2> piece in NarrowPieces(walkable, ops, radiusM))
            {
                if (count++ >= MaxPerLevel)
                {
                    break;
                }

                double width = OrientedBounds.Of(piece).ShortSideM;
                Point2 at = PolygonMath.Centroid(piece);
                findings.Add(new ValidationMessage(ValidationSeverity.Warning, string.Create(C,
                    $"Level '{level.Name}': a gap about {width:0.00} m wide near ({at.X:0.0}, {at.Y:0.0}) is narrower than the widest profile ({2 * radiusM:0.00} m across), so they can't pass it."))
                { Rule = ValidationRules.NarrowGap, LevelId = level.Id, At = at });
            }
        }

        return findings;
    }

    /// <summary>The parts of <paramref name="walkable"/> a disc of <paramref name="radiusM"/> can't reach, biggest first.</summary>
    public static List<IReadOnlyList<Point2>> NarrowPieces(IReadOnlyList<IReadOnlyList<Point2>> walkable, IPolygonOps ops, double radiusM)
    {
        IReadOnlyList<IReadOnlyList<Point2>> shrunk = ops.Offset(walkable, -radiusM);
        IReadOnlyList<IReadOnlyList<Point2>> opened = shrunk.Count == 0 ? [] : ops.Offset(shrunk, radiusM);
        IReadOnlyList<IReadOnlyList<Point2>> lost = opened.Count == 0 ? walkable : ops.Difference(walkable, opened);
        return [.. lost
            .Where(p => p.Count >= 3 && PolygonMath.SignedArea(p) > 0 && PolygonMath.Area(p) >= MinPieceM2)
            .OrderByDescending(PolygonMath.Area)];
    }
}
