namespace Throng.Core.Editing;

/// <summary>
/// A reversible change to the study. Every edit goes through one of these so undo/redo is unbounded and exact.
/// </summary>
public interface IUndoableAction
{
    /// <summary>Short description for menus and the status ticker, e.g. "Rename level".</summary>
    string Description { get; }

    /// <summary>Applies (or re-applies) the change.</summary>
    void Do();

    /// <summary>Reverts the change.</summary>
    void Undo();
}

/// <summary>
/// Generic property edit: captures the old value on construction and swaps between old and new.
/// </summary>
/// <typeparam name="T">Property type.</typeparam>
public sealed class PropertyChangeAction<T> : IUndoableAction
{
    private readonly Action<T> _setter;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action? _changed;

    /// <summary>Creates the action. The current value is read immediately as the undo value.</summary>
    /// <param name="description">Menu/ticker description.</param>
    /// <param name="getter">Reads the current value.</param>
    /// <param name="setter">Writes a value to the model.</param>
    /// <param name="newValue">Value to apply.</param>
    /// <param name="changed">Called after every do/undo so views can refresh.</param>
    public PropertyChangeAction(string description, Func<T> getter, Action<T> setter, T newValue, Action? changed)
    {
        Description = description;
        _setter = setter;
        _oldValue = getter();
        _newValue = newValue;
        _changed = changed;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>True when applying would change nothing (callers can skip pushing it).</summary>
    public bool IsNoOp => EqualityComparer<T>.Default.Equals(_oldValue, _newValue);

    /// <inheritdoc />
    public void Do()
    {
        _setter(_newValue);
        _changed?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _setter(_oldValue);
        _changed?.Invoke();
    }
}
