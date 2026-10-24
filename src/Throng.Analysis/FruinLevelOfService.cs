namespace Throng.Analysis;

/// <summary>Fruin Level of Service grade, A (free) to F (critical).</summary>
public enum LevelOfService
{
    A,
    B,
    C,
    D,
    E,
    F,
}

/// <summary>Which Fruin table applies to an area (§11.3).</summary>
public enum LosAreaType
{
    /// <summary>Corridors, concourses and open floor.</summary>
    Walkway,

    /// <summary>Queueing and waiting areas.</summary>
    Queue,

    /// <summary>Stairs and other connectors.</summary>
    Stair,
}

/// <summary>
/// Classifies pedestrian density into Fruin Level of Service grades by area per pedestrian (m²/ped).
/// Thresholds follow Fruin (1971) as tabulated in the brief and should be verified against source before
/// results are shared (see THIRD-PARTY-NOTICES acknowledgements).
/// </summary>
public static class FruinLevelOfService
{
    // Lower bounds (m²/ped) for grades A..E; anything below the E bound is F.
    private static readonly double[] WalkwayBounds = [3.25, 2.32, 1.39, 0.93, 0.46];
    private static readonly double[] QueueBounds = [1.21, 0.93, 0.65, 0.28, 0.19];
    private static readonly double[] StairBounds = [1.86, 1.39, 0.93, 0.65, 0.37];

    /// <summary>Grade for a given area per pedestrian. Larger areas are better; A requires strictly more than the A bound.</summary>
    public static LevelOfService FromAreaPerPedestrian(double areaM2PerPed, LosAreaType type)
    {
        double[] bounds = type switch
        {
            LosAreaType.Queue => QueueBounds,
            LosAreaType.Stair => StairBounds,
            _ => WalkwayBounds,
        };

        for (int i = 0; i < bounds.Length; i++)
        {
            if (areaM2PerPed > bounds[i])
            {
                return (LevelOfService)i;
            }
        }

        return LevelOfService.F;
    }

    /// <summary>Grade for a density in persons per m² (zero density is grade A).</summary>
    public static LevelOfService FromDensity(double personsPerM2, LosAreaType type) =>
        personsPerM2 <= 0 ? LevelOfService.A : FromAreaPerPedestrian(1.0 / personsPerM2, type);

    /// <summary>Plain-language label for non-specialist audiences (D-012 toggle).</summary>
    public static string AbstractLabel(LevelOfService los) => los switch
    {
        LevelOfService.A or LevelOfService.B => "Low",
        LevelOfService.C or LevelOfService.D => "Medium",
        LevelOfService.E => "High",
        _ => "Critical",
    };
}
