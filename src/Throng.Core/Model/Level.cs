namespace Throng.Core.Model;

/// <summary>
/// An authoring plane at a given elevation. Levels organise sketching and visibility; the simulation itself
/// works on compiled surfaces (see <see cref="World.Surface"/>), so any elevation is allowed (mezzanines, split levels).
/// </summary>
public sealed class Level
{
    /// <summary>Stable identifier; regions and connectors reference levels by this.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, e.g. "Concourse".</summary>
    public string Name { get; set; } = "Level";

    /// <summary>Finished floor level in metres (world Z).</summary>
    public double ElevationM { get; set; }

    /// <summary>Floor-to-floor height in metres; used for 3D display and connector sizing.</summary>
    public double HeightM { get; set; } = 4.0;

    /// <summary>Shown in views when true.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Locked levels can't be edited (enforced by editor tools from M1).</summary>
    public bool IsLocked { get; set; }
}
