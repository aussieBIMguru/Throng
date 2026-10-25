using Throng.Core.Geometry;

namespace Throng.Core.Model;

/// <summary>
/// A vertical opening drawn once for the whole building (D-056): a lift shaft, atrium, stair well or riser. Its
/// outline is subtracted from the walkable area of every level whose elevation lies between the base and top
/// levels, inclusive.
/// </summary>
/// <remarks>
/// The outline's vertex order is kept as drawn (only duplicate/collinear points are cleaned), so lift doors can
/// later map "front" and "rear" to specific edges (D-068, M4).
/// </remarks>
public sealed class Shaft
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, e.g. "Shaft 1".</summary>
    public string Name { get; set; } = "Shaft";

    /// <summary>Lowest level the shaft passes through.</summary>
    public Guid BaseLevelId { get; set; }

    /// <summary>Highest level the shaft passes through.</summary>
    public Guid TopLevelId { get; set; }

    /// <summary>Closed outline in plan metres.</summary>
    public List<Point2> Outline { get; set; } = [];

    /// <summary>Optional free-text tag, e.g. "Lift" or "Atrium".</summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Spanned levels where a lift would not stop (D-087). The floor is still cut there; M4 lifts get no landing
    /// or doors on these levels.
    /// </summary>
    public List<Guid> NoStopLevelIds { get; set; } = [];

    /// <summary>Returns an independent copy, including the id.</summary>
    public Shaft Clone()
    {
        var copy = new Shaft();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>Copies every value (including the id) from <paramref name="other"/>; used by undo snapshots.</summary>
    public void CopyFrom(Shaft other)
    {
        Id = other.Id;
        Name = other.Name;
        BaseLevelId = other.BaseLevelId;
        TopLevelId = other.TopLevelId;
        Outline = [.. other.Outline];
        Tag = other.Tag;
        NoStopLevelIds = [.. other.NoStopLevelIds];
    }
}
