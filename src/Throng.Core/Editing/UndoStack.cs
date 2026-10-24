namespace Throng.Core.Editing;

/// <summary>
/// Unbounded undo/redo history with save-point tracking for the "unsaved changes" marker.
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<IUndoableAction> _done = new();
    private readonly Stack<IUndoableAction> _undone = new();
    private IUndoableAction? _savedTop;

    /// <summary>Raised after any execute, undo, redo, clear or save-point change.</summary>
    public event EventHandler? Changed;

    /// <summary>True when there is something to undo.</summary>
    public bool CanUndo => _done.Count > 0;

    /// <summary>True when there is something to redo.</summary>
    public bool CanRedo => _undone.Count > 0;

    /// <summary>Description of the next undo, if any.</summary>
    public string? UndoDescription => _done.Count > 0 ? _done.Peek().Description : null;

    /// <summary>Description of the next redo, if any.</summary>
    public string? RedoDescription => _undone.Count > 0 ? _undone.Peek().Description : null;

    /// <summary>
    /// True when the document differs from the last save point. Undoing back to the save point makes it clean again.
    /// </summary>
    public bool IsDirty => !ReferenceEquals(_done.Count > 0 ? _done.Peek() : null, _savedTop);

    /// <summary>Applies an action and records it. Clears the redo history.</summary>
    public void Execute(IUndoableAction action)
    {
        action.Do();
        _done.Push(action);
        _undone.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reverts the most recent action. Returns false when there is nothing to undo.</summary>
    public bool Undo()
    {
        if (_done.Count == 0)
        {
            return false;
        }

        IUndoableAction action = _done.Pop();
        action.Undo();
        _undone.Push(action);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Re-applies the most recently undone action. Returns false when there is nothing to redo.</summary>
    public bool Redo()
    {
        if (_undone.Count == 0)
        {
            return false;
        }

        IUndoableAction action = _undone.Pop();
        action.Do();
        _done.Push(action);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Marks the current state as saved.</summary>
    public void MarkSaved()
    {
        _savedTop = _done.Count > 0 ? _done.Peek() : null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets all history (new/open document). The empty state counts as saved.</summary>
    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
        _savedTop = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
