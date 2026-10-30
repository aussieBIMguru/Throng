using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.Core.Editing;

/// <summary>Makes a lift bank for a shaft from a lift preset (round 013, D-236).</summary>
public static class LiftFactory
{
    /// <summary>
    /// A new lift (not yet put in the shaft): one car, front doors on the shaft's first drawn edge (D-242) at every
    /// served level, no rear doors, a 2.5 m landing (D-241), the lowest served level as lobby (D-244) and the preset's
    /// dispatch (Destination dispatch runs as Collective until it's built).
    /// </summary>
    /// <param name="shaft">The shaft it goes in.</param>
    /// <param name="template">The preset.</param>
    /// <param name="templateId">Id of the preset's copy embedded in the study library (or the preset's own id).</param>
    public static Lift FromTemplate(Shaft shaft, LiftTemplate template, Guid templateId) => new()
    {
        Name = shaft.Name + " lift",
        TemplateId = templateId,
        PresetKey = template.Key,
        Cars = 1,
        FrontEdge = 0,
        LandingDepthM = Lift.DefaultLandingDepthM,
        Dispatch = template.Dispatch,
    };
}
