using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Core.Editing;

/// <summary>
/// Adds regions to a study (draw, paste, duplicate). Undo removes the same instances, so anything holding a
/// reference to them (property panel, later actions) stays valid across undo/redo.
/// </summary>
public sealed class AddRegionsAction : IUndoableAction
{
    private readonly Study _study;
    private readonly Region[] _regions;
    private readonly Action? _changed;

    /// <summary>Creates the action.</summary>
    /// <param name="description">Menu/ticker description.</param>
    /// <param name="study">Study to add to.</param>
    /// <param name="regions">Regions to add (new instances not yet in the study).</param>
    /// <param name="changed">Called after every do/undo.</param>
    public AddRegionsAction(string description, Study study, IEnumerable<Region> regions, Action? changed)
    {
        Description = description;
        _study = study;
        _regions = [.. regions];
        _changed = changed;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>The regions this action adds.</summary>
    public IReadOnlyList<Region> Regions => _regions;

    /// <inheritdoc />
    public void Do()
    {
        _study.Regions.AddRange(_regions);
        _changed?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach (Region region in _regions)
        {
            _study.Regions.Remove(region);
        }

        _changed?.Invoke();
    }
}

/// <summary>
/// Removes regions from a study. Undo puts the same instances back at their original list positions, so draw
/// order and file order round-trip exactly.
/// </summary>
public sealed class RemoveRegionsAction : IUndoableAction
{
    private readonly Study _study;
    private readonly (Region Region, int Index)[] _entries;
    private readonly Action? _changed;

    /// <summary>Creates the action; positions are captured now.</summary>
    /// <param name="description">Menu/ticker description.</param>
    /// <param name="study">Study to remove from.</param>
    /// <param name="regions">Regions to remove; ones not in the study are ignored.</param>
    /// <param name="changed">Called after every do/undo.</param>
    public RemoveRegionsAction(string description, Study study, IEnumerable<Region> regions, Action? changed)
    {
        Description = description;
        _study = study;
        _changed = changed;
        _entries = [.. regions
            .Distinct()
            .Select(r => (Region: r, Index: study.Regions.IndexOf(r)))
            .Where(e => e.Index >= 0)
            .OrderBy(e => e.Index)];
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>Number of regions this action removes.</summary>
    public int Count => _entries.Length;

    /// <inheritdoc />
    public void Do()
    {
        foreach ((Region region, _) in _entries)
        {
            _study.Regions.Remove(region);
        }

        _changed?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        // Ascending order restores each index against a list that already holds every earlier entry.
        foreach ((Region region, int index) in _entries)
        {
            _study.Regions.Insert(Math.Min(index, _study.Regions.Count), region);
        }

        _changed?.Invoke();
    }
}

/// <summary>
/// Replaces the outlines of one or more regions (move, vertex drag, vertex insert/delete). Old and new outlines
/// are copied, so later edits to either list can't leak into history.
/// </summary>
public sealed class ReplaceOutlinesAction : IUndoableAction
{
    private readonly (Region Region, Point2[] Old, Point2[] New)[] _changes;
    private readonly Action? _changed;

    /// <summary>Creates the action; current outlines are captured now as the undo state.</summary>
    /// <param name="description">Menu/ticker description.</param>
    /// <param name="changes">Each region and the outline it should get.</param>
    /// <param name="changed">Called after every do/undo.</param>
    public ReplaceOutlinesAction(string description, IEnumerable<(Region Region, IReadOnlyList<Point2> NewOutline)> changes, Action? changed)
    {
        Description = description;
        _changed = changed;
        _changes = [.. changes.Select(c => (c.Region, c.Region.Outline.ToArray(), c.NewOutline.ToArray()))];
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>True when no outline would change.</summary>
    public bool IsNoOp => _changes.All(c => c.Old.AsSpan().SequenceEqual(c.New));

    /// <inheritdoc />
    public void Do()
    {
        foreach ((Region region, _, Point2[] next) in _changes)
        {
            region.Outline = [.. next];
        }

        _changed?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach ((Region region, Point2[] old, _) in _changes)
        {
            region.Outline = [.. old];
        }

        _changed?.Invoke();
    }
}
