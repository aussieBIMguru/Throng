namespace Throng.Sim.Engine;

/// <summary>Which value of the density map a reader wants (live, round 011a; cumulative, round 011b, D-216).</summary>
public enum DensityMeasure
{
    /// <summary>Smoothed people per square metre right now.</summary>
    Live,

    /// <summary>Highest smoothed density each cell reached after the warm-up, people per square metre.</summary>
    Max,

    /// <summary>Mean smoothed density after the warm-up, people per square metre.</summary>
    Mean,

    /// <summary>Seconds after the warm-up each cell spent at Fruin LoS E or worse, by the table for that cell.</summary>
    LosEOrWorseS,

    /// <summary>People who walked onto each cell after the warm-up (footfall trail).</summary>
    Footfall,
}
