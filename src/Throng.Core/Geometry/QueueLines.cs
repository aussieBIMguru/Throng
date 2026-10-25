using Throng.Core.Model;

namespace Throng.Core.Geometry;

/// <summary>
/// Builds queue slots for a device against a walkability test, so the simulation (floor grid) and the editor preview
/// (walkable polygons) lay them out the same way.
/// </summary>
public static class QueueLines
{
    /// <summary>Spacing of queue slots along a line, metres (accepted default, D-159).</summary>
    public const double SlotSpacingM = 0.6;

    /// <summary>Distance of a service point in front of the footprint edge, metres.</summary>
    public const double ServiceOffsetM = 0.4;

    /// <summary>Half the width a person needs to stand in a line, metres.</summary>
    private const double HalfBodyM = 0.25;

    /// <summary>Upper limit on slots per line, whatever <see cref="Device.MaxQueue"/> says.</summary>
    public const int MaxSlots = 500;

    /// <summary>The service point of a lane on a side (side 1 is the far side of a pass-through device).</summary>
    public static Point2 LaneStart(Device device, int side, int lane)
    {
        int lanes = Math.Max(1, device.Lanes);
        Point2 facing = device.Facing();
        Point2 outward = side == 0 ? facing : facing * -1;
        double t = (-device.WidthM * 0.5) + (device.WidthM * (lane + 0.5) / lanes);
        return device.Position + (outward * ((device.DepthM * 0.5) + ServiceOffsetM)) + (facing.Perpendicular() * t);
    }

    /// <summary>Where a drawn queue path starts: the middle of the service side, at the service points' offset.</summary>
    public static Point2 QueueStart(Device device) =>
        device.Position + (device.Facing() * ((device.DepthM * 0.5) + ServiceOffsetM));

    /// <summary>
    /// Every queue line of <paramref name="device"/>: automatic lines straight out from each service point (bending
    /// along walls), or, with a drawn path of two or more points, a shared snake on the service side (D-163).
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="walkable">True where a person can stand.</param>
    public static QueueLineSet Build(Device device, Func<Point2, bool> walkable)
    {
        int lanes = Math.Max(1, device.Lanes);
        int sides = device.PassThrough ? 2 : 1;
        int max = Math.Clamp(device.MaxQueue, 1, MaxSlots);
        bool snake = device.QueuePath is { Count: >= 2 };
        Point2 facing = device.Facing();
        var lines = new Point2[sides * lanes][];
        for (int side = 0; side < sides; side++)
        {
            Point2 outward = side == 0 ? facing : facing * -1;
            for (int lane = 0; lane < lanes; lane++)
            {
                Point2 start = LaneStart(device, side, lane);
                lines[(side * lanes) + lane] = side == 0 && snake
                    ? (Clear(walkable, start, outward) ? [start] : [])
                    : Auto(walkable, start, outward, max);
            }
        }

        // The snake's cap covers every lane it feeds.
        Point2[]? snakeSlots = snake ? AlongPath(walkable, device.QueuePath, Math.Clamp(max * lanes, 1, MaxSlots)) : null;
        return new QueueLineSet(lines, snakeSlots);
    }

    /// <summary>
    /// Automatic queue line (D-153): slots every <see cref="SlotSpacingM"/> straight out from the service point;
    /// where the floor ends it turns 90° (left first, then right) and carries on. Stops at <paramref name="max"/>
    /// slots or when it can't continue.
    /// </summary>
    public static Point2[] Auto(Func<Point2, bool> walkable, Point2 start, Point2 direction, int max)
    {
        var slots = new List<Point2>(Math.Min(max, 64));
        if (!Clear(walkable, start, direction))
        {
            return [];
        }

        slots.Add(start);
        Point2 dir = direction;
        Point2 at = start;
        while (slots.Count < max)
        {
            Point2 straight = at + (dir * SlotSpacingM);
            if (Clear(walkable, straight, dir))
            {
                at = straight;
                slots.Add(at);
                continue;
            }

            Point2 turnLeft = dir.Perpendicular();
            Point2 turnRight = turnLeft * -1;
            if (Clear(walkable, at + (turnLeft * SlotSpacingM), turnLeft))
            {
                dir = turnLeft;
            }
            else if (Clear(walkable, at + (turnRight * SlotSpacingM), turnRight))
            {
                dir = turnRight;
            }
            else
            {
                break;
            }

            at += dir * SlotSpacingM;
            slots.Add(at);
        }

        return [.. slots];
    }

    /// <summary>Slots every <see cref="SlotSpacingM"/> along a drawn path, up to the first point off the floor.</summary>
    public static Point2[] AlongPath(Func<Point2, bool> walkable, IReadOnlyList<Point2> path, int max)
    {
        var slots = new List<Point2>(Math.Min(max, 64));
        double carried = 0;
        for (int k = 0; k + 1 < path.Count && slots.Count < max; k++)
        {
            Point2 a = path[k], b = path[k + 1];
            double length = Point2.Distance(a, b);
            if (length < 1e-6)
            {
                continue;
            }

            Point2 dir = (b - a) * (1 / length);
            double d = carried;
            while (d <= length + 1e-9 && slots.Count < max)
            {
                Point2 p = a + (dir * d);
                if (!walkable(p))
                {
                    return [.. slots];
                }

                slots.Add(p);
                d += SlotSpacingM;
            }

            // How far into the next segment the next slot falls.
            carried = d - length;
        }

        return [.. slots];
    }

    /// <summary>True when a person (about 0.5 m wide) could stand at <paramref name="p"/> facing along <paramref name="dir"/>.</summary>
    public static bool Clear(Func<Point2, bool> walkable, Point2 p, Point2 dir)
    {
        Point2 side = dir.Perpendicular() * HalfBodyM;
        return walkable(p) && walkable(p + side) && walkable(p - side);
    }
}
