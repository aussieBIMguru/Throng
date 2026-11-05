namespace Throng.Sim.Engine;

/// <summary>Live density map (M5, round 011a, D-201–D-205): sampled at the end of each step.</summary>
public sealed partial class SimEngine
{
    private DensityGrid? _density;

    /// <summary>The live density map; built on first use.</summary>
    public DensityGrid Density => _density ??= new DensityGrid(_model);

    /// <summary>Samples the map when its interval has passed (cheap otherwise).</summary>
    private void SampleDensity() => Density.Sample(_agents, TimeS);
}
