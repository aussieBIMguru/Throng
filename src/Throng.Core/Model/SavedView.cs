namespace Throng.Core.Model;

/// <summary>
/// A saved named view (round 016, D-276, format v10): how the viewport looked when it was saved, so clicking it in
/// STUDY ▸ VIEW ▸ VIEWS restores it. Camera values are those of the plan or orbit/ISO camera for <see cref="Mode"/>.
/// </summary>
public sealed class SavedView
{
    /// <summary>Stable id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "View";

    /// <summary>"Plan", "Orbit" or "Iso".</summary>
    public string Mode { get; set; } = "Orbit";

    /// <summary>Active level (plan views, and the level shown as active in 3D).</summary>
    public Guid? LevelId { get; set; }

    /// <summary>Camera target (orbit / ISO) or plan centre (X, Y; Z = 0), metres.</summary>
    public double TargetX { get; set; }

    /// <summary>Camera target Y, metres.</summary>
    public double TargetY { get; set; }

    /// <summary>Camera target Z, metres.</summary>
    public double TargetZ { get; set; }

    /// <summary>Orbit heading, radians.</summary>
    public double Yaw { get; set; }

    /// <summary>Orbit elevation, radians.</summary>
    public double Pitch { get; set; }

    /// <summary>Orbit / ISO distance, metres; for plan views, metres per DIP.</summary>
    public double Distance { get; set; }

    /// <summary>ISO preset 0–3 (SW, SE, NE, NW).</summary>
    public int IsoPreset { get; set; }

    /// <summary>Clip above <see cref="ClipLevelId"/> on.</summary>
    public bool ClipEnabled { get; set; }

    /// <summary>The clip level.</summary>
    public Guid? ClipLevelId { get; set; }

    /// <summary>Section box on.</summary>
    public bool SectionBox { get; set; }

    /// <summary>Section box sides, percent of the study's extents: west, east, south, north, bottom, top.</summary>
    public double[] Box { get; set; } = [0, 100, 0, 100, 0, 100];

    /// <summary>Extra height between levels, metres.</summary>
    public double ExplodeM { get; set; }

    /// <summary>A deep copy (undo snapshots).</summary>
    public SavedView Clone()
    {
        var copy = (SavedView)MemberwiseClone();
        copy.Box = [.. Box];
        return copy;
    }
}
