using Throng.Core.Model;

namespace Throng.Core.Editing;

/// <summary>
/// Adds items to a study list (connectors, shafts). Undo removes the same instances, so references held by the
/// property panel or later actions stay valid across undo/redo.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class AddItemsAction<T> : IUndoableAction
    where T : class
{
    private readonly List<T> _list;
    private readonly T[] _items;
    private readonly Action? _changed;

    /// <summary>Creates the action.</summary>
    /// <param name="description">Menu/ticker description.</param>
    /// <param name="list">Study list to add to.</param>
    /// <param name="items">New instances not yet in the list.</param>
    /// <param name="changed">Called after every do/undo.</param>
    public AddItemsAction(string description, List<T> list, IEnumerable<T> items, Action? changed)
    {
        Description = description;
        _list = list;
        _items = [.. items];
        _changed = changed;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>The items this action adds.</summary>
    public IReadOnlyList<T> Items => _items;

    /// <inheritdoc />
    public void Do()
    {
        _list.AddRange(_items);
        _changed?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach (T item in _items)
        {
            _list.Remove(item);
        }

        _changed?.Invoke();
    }
}

/// <summary>
/// Removes items from a study list. Undo puts the same instances back at their original positions.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class RemoveItemsAction<T> : IUndoableAction
    where T : class
{
    private readonly List<T> _list;
    private readonly (T Item, int Index)[] _entries;
    private readonly Action? _changed;

    /// <summary>Creates the action; positions are captured now. Items not in the list are ignored.</summary>
    public RemoveItemsAction(string description, List<T> list, IEnumerable<T> items, Action? changed)
    {
        Description = description;
        _list = list;
        _changed = changed;
        _entries = [.. items
            .Distinct()
            .Select(i => (Item: i, Index: list.IndexOf(i)))
            .Where(e => e.Index >= 0)
            .OrderBy(e => e.Index)];
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>Number of items this action removes.</summary>
    public int Count => _entries.Length;

    /// <inheritdoc />
    public void Do()
    {
        foreach ((T item, _) in _entries)
        {
            _list.Remove(item);
        }

        _changed?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach ((T item, int index) in _entries)
        {
            _list.Insert(Math.Min(index, _list.Count), item);
        }

        _changed?.Invoke();
    }
}

/// <summary>
/// Replaces the whole state of one or more objects with new values (move, rotate, reshape, multi-property edits).
/// Old and new states are snapshots, so later edits to either can't leak into history.
/// </summary>
/// <typeparam name="T">Object type.</typeparam>
public sealed class ReplaceStateAction<T> : IUndoableAction
    where T : class
{
    private readonly (T Target, T Old, T New)[] _changes;
    private readonly Action<T, T> _copyInto;
    private readonly Action? _changed;

    /// <summary>Creates the action; current states are snapshotted now as the undo state.</summary>
    /// <param name="description">Menu/ticker description.</param>
    /// <param name="changes">Each target and the state it should take (the state object isn't kept).</param>
    /// <param name="clone">Deep copy of a state.</param>
    /// <param name="copyInto">Copies the second argument's values into the first.</param>
    /// <param name="changed">Called after every do/undo.</param>
    public ReplaceStateAction(string description, IEnumerable<(T Target, T NewState)> changes, Func<T, T> clone, Action<T, T> copyInto, Action? changed)
    {
        Description = description;
        _copyInto = copyInto;
        _changed = changed;
        _changes = [.. changes.Select(c => (c.Target, clone(c.Target), clone(c.NewState)))];
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>Number of objects changed.</summary>
    public int Count => _changes.Length;

    /// <inheritdoc />
    public void Do()
    {
        foreach ((T target, _, T next) in _changes)
        {
            _copyInto(target, next);
        }

        _changed?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        foreach ((T target, T old, _) in _changes)
        {
            _copyInto(target, old);
        }

        _changed?.Invoke();
    }
}

/// <summary>Factory helpers for state snapshots of connectors and shafts.</summary>
public static class StateActions
{
    /// <summary>Replaces connector states (move, rotate, pair edits).</summary>
    public static ReplaceStateAction<Connector> Connectors(string description, IEnumerable<(Connector Target, Connector NewState)> changes, Action? changed) =>
        new(description, changes, c => c.Clone(), (target, source) => target.CopyFrom(source), changed);

    /// <summary>Replaces device states (move, rotate, resize).</summary>
    public static ReplaceStateAction<Device> Devices(string description, IEnumerable<(Device Target, Device NewState)> changes, Action? changed) =>
        new(description, changes, d => d.Clone(), (target, source) => target.CopyFrom(source), changed);

    /// <summary>Replaces underlay states (move, rotate, calibrate).</summary>
    public static ReplaceStateAction<Underlay> Underlays(string description, IEnumerable<(Underlay Target, Underlay NewState)> changes, Action? changed) =>
        new(description, changes, u => u.Clone(), (target, source) => target.CopyFrom(source), changed);

    /// <summary>Replaces shaft states (move, reshape).</summary>
    public static ReplaceStateAction<Shaft> Shafts(string description, IEnumerable<(Shaft Target, Shaft NewState)> changes, Action? changed) =>
        new(description, changes, s => s.Clone(), (target, source) => target.CopyFrom(source), changed);
}

/// <summary>
/// Several actions applied as one undo step (Copy to Levels, deleting a mixed selection, moving regions and
/// connectors together). Sub-actions should be created without their own change callback; this action calls
/// its own once after doing or undoing them all.
/// </summary>
public sealed class CompositeAction : IUndoableAction
{
    private readonly IUndoableAction[] _actions;
    private readonly Action? _changed;

    /// <summary>Creates the action.</summary>
    /// <param name="description">Menu/ticker description.</param>
    /// <param name="actions">Actions in the order they are applied; undo runs them in reverse.</param>
    /// <param name="changed">Called once after every do/undo.</param>
    public CompositeAction(string description, IEnumerable<IUndoableAction> actions, Action? changed)
    {
        Description = description;
        _actions = [.. actions];
        _changed = changed;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>True when there is nothing to apply.</summary>
    public bool IsEmpty => _actions.Length == 0;

    /// <inheritdoc />
    public void Do()
    {
        foreach (IUndoableAction action in _actions)
        {
            action.Do();
        }

        _changed?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        for (int i = _actions.Length - 1; i >= 0; i--)
        {
            _actions[i].Undo();
        }

        _changed?.Invoke();
    }
}
