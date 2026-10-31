using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.App.Editing;

/// <summary>
/// What the 3D / ISO viewport needs to drive the editor (round 017, D-291; select-only since 017b, D-298). The
/// viewport turns the pointer into a point on a level's floor plane and calls the usual pointer methods, so picking
/// and the hover label are the plan editor's own; with <see cref="Is3D"/> set, a press never becomes a move. This
/// part adds the floor test the viewport's pick uses, level activation, and the pick for the right-click menu.
/// </summary>
public sealed partial class PlanEditor
{
    /// <summary>True while the 3D / ISO view drives the editor: it picks only, no moves, grips or rubber band (D-298).</summary>
    public bool Is3D { get; set; }

    /// <summary>Id of the level being edited, or null.</summary>
    public Guid? ActiveLevelId => _host.ActiveLevelModel?.Id;

    /// <summary>Elevation of the level being edited, or null.</summary>
    public double? ActiveElevationM => _host.ActiveLevelModel?.ElevationM;

    /// <summary>
    /// True when a level has something at a plan point: a region present on it (floors, voids, zones, obstacles, also
    /// spanning ones) or a device. The 3D pick uses it to find the floor under the cursor (D-293).
    /// </summary>
    public bool HasFloorAt(Guid levelId, Point2 point)
    {
        foreach (Region region in _host.Study.RegionsPresentOn(levelId))
        {
            if (region.Outline is { Count: >= 3 } outline && PolygonMath.Contains(outline, point))
            {
                return true;
            }
        }

        foreach (Device device in _host.Study.DevicesOn(levelId))
        {
            if (PolygonMath.Contains(DeviceGeometry.Footprint(device), point))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Makes a level the active one (no-op when it already is). True when it's active afterwards.</summary>
    public bool ActivateLevel(Guid levelId) => _host.ActiveLevelModel?.Id == levelId || _host.ActivateLevel(levelId);

    /// <summary>
    /// The object under a floor point, for the 3D right-click menu (round 017b, D-299); null over nothing.
    /// </summary>
    public Guid? ObjectAt(Point2 point, double metresPerDip)
    {
        _metresPerDip = metresPerDip;
        return PickAt(point);
    }
}
