namespace Throng.Core.Geometry;

/// <summary>
/// Exploded levels (round 015, D-264): heights are remapped so each level sits <c>gap</c> metres higher than the one
/// below it. Level content moves up rigidly (<see cref="LevelShift"/>); anything spanning levels (stairs, shafts, lift
/// cars, people on them) uses <see cref="Warp"/>, which is linear within each storey and agrees with the rigid shift at
/// every level's floor, so spanning objects stretch to bridge the gap. The shaders implement the same functions; this
/// copy is for the CPU (fitting the camera, tests).
/// </summary>
public static class ExplodeMap
{
    /// <summary>Most level elevations the shaders take; levels above share the top one's shift.</summary>
    public const int MaxLevels = 64;

    /// <summary>Distinct elevations, lowest first, at most <see cref="MaxLevels"/> (closer than 1 mm count as one).</summary>
    public static double[] Elevations(IEnumerable<double> elevations)
    {
        var sorted = elevations.Where(double.IsFinite).OrderBy(z => z).ToList();
        var distinct = new List<double>(sorted.Count);
        foreach (double z in sorted)
        {
            if (distinct.Count == 0 || z - distinct[^1] > 1e-3)
            {
                distinct.Add(z);
            }
        }

        return [.. distinct.Take(MaxLevels)];
    }

    /// <summary>
    /// How far a level at <paramref name="elevation"/> moves up: <paramref name="gap"/> × the number of distinct
    /// elevations below it.
    /// </summary>
    public static double LevelShift(double elevation, IReadOnlyList<double> elevations, double gap)
    {
        if (gap <= 0 || elevations.Count < 2)
        {
            return 0;
        }

        int k = 0;
        for (int i = 1; i < elevations.Count; i++)
        {
            if (elevation >= elevations[i] - 1e-3)
            {
                k = i;
            }
        }

        return k * gap;
    }

    /// <summary>
    /// The exploded height of <paramref name="z"/>: unchanged below the lowest level; within a storey from level i to
    /// level i + 1, stretched linearly so level i's floor lands at its <see cref="LevelShift"/> and level i + 1's at
    /// its own; above the top level, shifted like the top level.
    /// </summary>
    public static double Warp(double z, IReadOnlyList<double> elevations, double gap)
    {
        int n = elevations.Count;
        if (gap <= 0 || n < 2 || z <= elevations[0])
        {
            return z;
        }

        for (int i = 0; i < n - 1; i++)
        {
            double a = elevations[i], b = elevations[i + 1];
            if (z < b)
            {
                return a + (i * gap) + ((z - a) * (b - a + gap) / (b - a));
            }
        }

        return z + ((n - 1) * gap);
    }

    /// <summary>The true height drawn at exploded height <paramref name="zw"/>: the inverse of <see cref="Warp"/>.</summary>
    public static double Unwarp(double zw, IReadOnlyList<double> elevations, double gap)
    {
        int n = elevations.Count;
        if (gap <= 0 || n < 2 || zw <= elevations[0])
        {
            return zw;
        }

        for (int i = 0; i < n - 1; i++)
        {
            double a = elevations[i], b = elevations[i + 1];
            double wa = a + (i * gap), wb = b + ((i + 1) * gap);
            if (zw < wb)
            {
                return a + ((zw - wa) * (b - a) / (wb - wa));
            }
        }

        return zw - ((n - 1) * gap);
    }
}
