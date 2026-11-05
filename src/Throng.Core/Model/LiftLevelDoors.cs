namespace Throng.Core.Model;

/// <summary>A lift's doors at one served level (D-237): front and rear, each closed, entry, exit or both.</summary>
public sealed class LiftLevelDoors
{
    /// <summary>The level.</summary>
    public Guid LevelId { get; set; }

    /// <summary>Front door use (the shaft's front edge, D-242).</summary>
    public LiftDoorUse Front { get; set; } = LiftDoorUse.Both;

    /// <summary>Rear door use (the edge opposite the front).</summary>
    public LiftDoorUse Rear { get; set; } = LiftDoorUse.Closed;

    /// <summary>An independent copy.</summary>
    public LiftLevelDoors Clone() => new() { LevelId = LevelId, Front = Front, Rear = Rear };
}
