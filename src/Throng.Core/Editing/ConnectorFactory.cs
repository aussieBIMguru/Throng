using System.Globalization;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Core.Editing;

/// <summary>
/// Creates, copies and transforms connectors and shafts: placing from a preset (D-054, D-059), level shifting
/// for paste and Copy to Levels (D-060), and the move/rotate maths behind the editor (D-063).
/// </summary>
public static class ConnectorFactory
{
    /// <summary>Name prefix for new connectors of a kind.</summary>
    public static string NamePrefix(ConnectorKind kind) => kind switch
    {
        ConnectorKind.Stair => "Stair",
        ConnectorKind.Escalator => "Escalator",
        ConnectorKind.Ramp => "Ramp",
        _ => "Travelator",
    };

    /// <summary>
    /// Creates the connector(s) for a preset (not yet added to the study). A paired preset returns two linked
    /// units side by side (D-059): <paramref name="point"/> is then the midpoint between their bottom landings,
    /// the unit on the right (looking up) runs up and the one on the left runs down.
    /// </summary>
    /// <param name="study">Study, for unique names.</param>
    /// <param name="template">Preset to copy values from.</param>
    /// <param name="bottomLevelId">Bottom (active) level.</param>
    /// <param name="topLevelId">Target level.</param>
    /// <param name="point">Bottom landing centre (or the pair's midpoint).</param>
    /// <param name="angleDeg">Run direction.</param>
    /// <param name="side">Switchback return side.</param>
    /// <param name="runLengthM">Belt length for travelators (ignored otherwise).</param>
    public static List<Connector> FromTemplate(
        Study study, ConnectorTemplate template, Guid bottomLevelId, Guid topLevelId, Point2 point, double angleDeg, TurnSide side, double runLengthM = 20)
    {
        var names = new List<string>(study.Connectors.Select(c => c.Name));
        Connector Create(Point2 at, ConnectorDirection direction, Guid? pairId)
        {
            var connector = new Connector
            {
                Name = Naming.Next(names, NamePrefix(template.Kind)),
                TemplateId = template.Id,
                PresetKey = template.Key,
                Kind = template.Kind,
                BottomLevelId = bottomLevelId,
                TopLevelId = topLevelId,
                BottomLanding = at,
                AngleDeg = NormaliseAngle(angleDeg),
                WidthM = template.WidthM,
                Direction = direction,
                Shape = template.Kind == ConnectorKind.Escalator ? ConnectorShape.Straight : template.Shape,
                ReturnSide = side,
                ClearanceM = Connector.DefaultClearanceM,
                PairId = pairId,
                RiserM = template.RiserM,
                GoingM = template.GoingM,
                MaxRisersPerFlight = Math.Max(1, template.MaxRisersPerFlight),
                LandingM = template.LandingM,
                SwitchbackGapM = template.SwitchbackGapM,
                Gradient = template.Gradient > 0 ? template.Gradient : 1.0 / 14.0,
                MaxRampRunM = template.MaxRampRunM,
                InclineDeg = template.InclineDeg,
                FlatLengthM = template.FlatLengthM,
                PairGapM = template.PairGapM,
                PerLevelCircuits = true,
                RunLengthM = Math.Max(1, runLengthM),
            };
            names.Add(connector.Name);
            return connector;
        }

        if (!template.IsPaired)
        {
            ConnectorDirection direction = template.Kind switch
            {
                ConnectorKind.Escalator when template.Direction is not (ConnectorDirection.Up or ConnectorDirection.Down) => ConnectorDirection.Up,
                ConnectorKind.Travelator => ConnectorDirection.OneWay,
                _ => template.Direction,
            };
            return [Create(point, direction, null)];
        }

        Guid pair = Guid.NewGuid();
        Point2 left = Axis(angleDeg).Perpendicular() * PairSpacing(template.WidthM, template.PairGapM) * 0.5;
        return [Create(point - left, ConnectorDirection.Up, pair), Create(point + left, ConnectorDirection.Down, pair)];
    }

    /// <summary>Centre-to-centre spacing of a pair's units.</summary>
    public static double PairSpacing(double widthM, double gapM) => widthM + Math.Max(0, gapM);

    /// <summary>Unit vector for a plan angle in degrees.</summary>
    public static Point2 Axis(double angleDeg)
    {
        double radians = angleDeg * Math.PI / 180.0;
        return new Point2(Math.Cos(radians), Math.Sin(radians));
    }

    /// <summary>Angle in [0, 360).</summary>
    public static double NormaliseAngle(double angleDeg)
    {
        double a = angleDeg % 360.0;
        if (a < 0)
        {
            a += 360.0;
        }

        return Math.Abs(a - 360.0) < 1e-9 ? 0 : Math.Round(a, 6);
    }

    /// <summary>Angle of a plan vector in degrees, [0, 360).</summary>
    public static double AngleOf(Point2 vector) => NormaliseAngle(Math.Atan2(vector.Y, vector.X) * 180.0 / Math.PI);

    /// <summary>Snaps an angle to whole steps (e.g. 15°, D-073).</summary>
    public static double SnapAngle(double angleDeg, double stepDeg) =>
        stepDeg > 0 ? NormaliseAngle(Math.Round(angleDeg / stepDeg, MidpointRounding.AwayFromZero) * stepDeg) : NormaliseAngle(angleDeg);

    /// <summary>A copy with a new id and the given levels, moved by <paramref name="offset"/>.</summary>
    public static Connector Copy(Connector source, Guid bottomLevelId, Guid topLevelId, Point2 offset, Guid? pairId)
    {
        Connector copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.BottomLevelId = bottomLevelId;
        copy.TopLevelId = topLevelId;
        copy.BottomLanding = source.BottomLanding + offset;
        copy.PairId = pairId;
        return copy;
    }

    /// <summary>A snapshot of <paramref name="source"/> moved by <paramref name="delta"/> (same id).</summary>
    public static Connector Translated(Connector source, Point2 delta)
    {
        Connector next = source.Clone();
        next.BottomLanding = source.BottomLanding + delta;
        return next;
    }

    /// <summary>A snapshot of <paramref name="source"/> rotated about <paramref name="pivot"/> (same id).</summary>
    public static Connector Rotated(Connector source, Point2 pivot, double deltaDeg)
    {
        Connector next = source.Clone();
        next.BottomLanding = RotatePoint(source.BottomLanding, pivot, deltaDeg);
        next.AngleDeg = NormaliseAngle(source.AngleDeg + deltaDeg);
        return next;
    }

    /// <summary>Rotates a plan point about a pivot.</summary>
    public static Point2 RotatePoint(Point2 p, Point2 pivot, double deltaDeg)
    {
        double r = deltaDeg * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        Point2 d = p - pivot;
        return pivot + new Point2((d.X * cos) - (d.Y * sin), (d.X * sin) + (d.Y * cos));
    }

    /// <summary>Rotation pivot: the bottom landing, or the pair's midpoint for paired units.</summary>
    public static Point2 Pivot(Connector connector, Connector? partner) =>
        partner is null ? connector.BottomLanding : (connector.BottomLanding + partner.BottomLanding) * 0.5;

    /// <summary>
    /// Partner position after a width or gap change, keeping its side: the partner stays on the same side of
    /// <paramref name="connector"/> at the new spacing.
    /// </summary>
    public static Point2 RespacedPartner(Connector connector, Connector partner, double widthM, double gapM)
    {
        Point2 left = connector.Axis().Perpendicular();
        double side = Point2.Dot(partner.BottomLanding - connector.BottomLanding, left) >= 0 ? 1 : -1;
        return connector.BottomLanding + (left * (side * PairSpacing(widthM, gapM)));
    }

    /// <summary>
    /// Maps a level pair from <paramref name="fromLevelId"/> to <paramref name="toLevelId"/> by elevation order
    /// (D-060): a connector from level i to j pasted from i onto k goes from k to k + (j − i). Null when the
    /// shifted levels don't exist.
    /// </summary>
    public static (Guid Bottom, Guid Top)? ShiftLevels(Study study, Guid bottomLevelId, Guid topLevelId, Guid fromLevelId, Guid toLevelId)
    {
        IReadOnlyList<Level> ordered = study.LevelsAscending();
        int from = IndexOf(ordered, fromLevelId);
        int to = IndexOf(ordered, toLevelId);
        int bottom = IndexOf(ordered, bottomLevelId);
        int top = IndexOf(ordered, topLevelId);
        if (from < 0 || to < 0 || bottom < 0 || top < 0)
        {
            return null;
        }

        int shift = to - from;
        int b = bottom + shift, t = top + shift;
        return b < 0 || t < 0 || b >= ordered.Count || t >= ordered.Count ? null : (ordered[b].Id, ordered[t].Id);
    }

    /// <summary>Maps one level by the same elevation-order shift as <see cref="ShiftLevels"/>; null when it doesn't exist.</summary>
    public static Guid? ShiftLevel(Study study, Guid levelId, Guid fromLevelId, Guid toLevelId) =>
        ShiftLevels(study, levelId, levelId, fromLevelId, toLevelId)?.Bottom;

    /// <summary>
    /// A region copied onto another level keeps its span and exclusions shifted by the same number of levels;
    /// a span that would run past the top is clipped to the highest level.
    /// </summary>
    public static Region CopyRegion(Study study, Region source, Guid fromLevelId, Guid toLevelId, Point2 offset)
    {
        // A spanning region selected on an upper level keeps its base relative to that level.
        Guid baseLevel = fromLevelId == toLevelId ? source.LevelId : ShiftLevel(study, source.LevelId, fromLevelId, toLevelId) ?? toLevelId;
        Region copy = RegionFactory.Copy(source, baseLevel, offset);
        if (fromLevelId != toLevelId && source.TopLevelId is { } top)
        {
            IReadOnlyList<Level> ordered = study.LevelsAscending();
            copy.TopLevelId = ShiftLevel(study, top, fromLevelId, toLevelId) ?? (ordered.Count > 0 ? ordered[^1].Id : null);
            copy.ExcludedLevelIds = [.. source.ExcludedLevelIds
                .Select(id => ShiftLevel(study, id, fromLevelId, toLevelId))
                .Where(id => id is not null)
                .Select(id => id!.Value)];
        }

        return copy;
    }

    /// <summary>Creates a shaft (not yet added) with a unique name.</summary>
    public static Shaft CreateShaft(Study study, Guid baseLevelId, Guid topLevelId, IEnumerable<Point2> outline) => new()
    {
        Name = Naming.Next(study.Shafts.Select(s => s.Name), "Shaft"),
        BaseLevelId = baseLevelId,
        TopLevelId = topLevelId,
        Outline = [.. outline],
    };

    /// <summary>A shaft copy with a new id and the given levels, moved by <paramref name="offset"/>.</summary>
    public static Shaft CopyShaft(Shaft source, Guid baseLevelId, Guid topLevelId, Point2 offset)
    {
        Shaft copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.BaseLevelId = baseLevelId;
        copy.TopLevelId = topLevelId;
        copy.Outline = ShapeFactory.Translate(source.Outline, offset);
        if (source.BaseLevelId != baseLevelId)
        {
            // Shifted spans keep their "no stop" levels only where they still exist.
            copy.NoStopLevelIds = [];
        }

        return copy;
    }

    private static int IndexOf(IReadOnlyList<Level> levels, Guid id)
    {
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Creates and copies placed devices (D-089).</summary>
public static class DeviceFactory
{
    /// <summary>Creates a device from a point-device preset (not yet added to the study).</summary>
    public static Device FromTemplate(Study study, DeviceTemplate template, Guid levelId, Point2 position, double angleDeg) => new()
    {
        Name = Naming.Next(study.Devices.Select(d => d.Name), template.Name),
        TemplateId = template.Id,
        PresetKey = template.Key,
        TypeTag = template.TypeTag,
        LevelId = levelId,
        Position = position,
        AngleDeg = ConnectorFactory.NormaliseAngle(angleDeg),
        WidthM = Math.Max(0.1, template.WidthM),
        DepthM = Math.Max(0.1, template.DepthM),
        Lanes = Math.Max(1, template.Servers),
        PassThrough = template.PassThrough,
        HeightM = Device.DefaultHeightM,
        ColorHex = template.ColorHex,
    };

    /// <summary>A copy with a new id on the given level, moved by <paramref name="offset"/>.</summary>
    public static Device Copy(Device source, Guid levelId, Point2 offset)
    {
        Device copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.LevelId = levelId;
        copy.Position = source.Position + offset;
        return copy;
    }

    /// <summary>A snapshot moved by <paramref name="delta"/> (same id).</summary>
    public static Device Translated(Device source, Point2 delta)
    {
        Device next = source.Clone();
        next.Position = source.Position + delta;
        return next;
    }

    /// <summary>A snapshot rotated about <paramref name="pivot"/> (same id).</summary>
    public static Device Rotated(Device source, Point2 pivot, double deltaDeg)
    {
        Device next = source.Clone();
        next.Position = ConnectorFactory.RotatePoint(source.Position, pivot, deltaDeg);
        next.AngleDeg = ConnectorFactory.NormaliseAngle(source.AngleDeg + deltaDeg);
        return next;
    }
}

/// <summary>Readable unique names: "Prefix N", one above the highest N already used.</summary>
public static class Naming
{
    /// <summary>Next free "Prefix N" name among <paramref name="existing"/>.</summary>
    public static string Next(IEnumerable<string> existing, string prefix)
    {
        int highest = 0;
        string start = prefix + " ";
        foreach (string name in existing)
        {
            if (name.StartsWith(start, StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(start.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                && n > highest)
            {
                highest = n;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{prefix} {highest + 1}");
    }
}
