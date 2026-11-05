namespace Throng.Analysis;

/// <summary>What the live heatmap shows (D-201).</summary>
public enum HeatmapMode
{
    /// <summary>No heatmap.</summary>
    Off,

    /// <summary>People per square metre on a continuous ramp.</summary>
    Density,

    /// <summary>Fruin Level of Service bands, A to F, by the table for each place (D-205).</summary>
    Los,

    /// <summary>Run map: highest density each place reached after the warm-up, on the LoS bands (D-216).</summary>
    MaxDensity,

    /// <summary>Run map: mean density after the warm-up, on the density ramp (D-216).</summary>
    MeanDensity,

    /// <summary>Run map: time each place spent at LoS E or worse after the warm-up (D-216).</summary>
    LosEOrWorse,

    /// <summary>Run map: people who walked over each place after the warm-up (D-216).</summary>
    Footfall,
}
