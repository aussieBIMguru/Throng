using Throng.Core.Editing;

namespace Throng.Core.Tests;

[TestClass]
public sealed class UndoStackTests
{
    private int _value;

    private PropertyChangeAction<int> Set(int value) => new("Set value", () => _value, v => _value = v, value, null);

    [TestMethod]
    public void ExecuteUndoRedo_RestoresValues()
    {
        var stack = new UndoStack();
        stack.Execute(Set(1));
        stack.Execute(Set(2));
        Assert.AreEqual(2, _value);

        Assert.IsTrue(stack.Undo());
        Assert.AreEqual(1, _value);
        Assert.IsTrue(stack.Undo());
        Assert.AreEqual(0, _value);
        Assert.IsFalse(stack.Undo(), "Nothing left to undo.");

        Assert.IsTrue(stack.Redo());
        Assert.AreEqual(1, _value);
    }

    [TestMethod]
    public void NewAction_ClearsRedo()
    {
        var stack = new UndoStack();
        stack.Execute(Set(1));
        stack.Undo();
        Assert.IsTrue(stack.CanRedo);

        stack.Execute(Set(5));
        Assert.IsFalse(stack.CanRedo);
    }

    [TestMethod]
    public void Dirty_TracksSavePoint()
    {
        var stack = new UndoStack();
        Assert.IsFalse(stack.IsDirty, "A fresh document is clean.");

        stack.Execute(Set(1));
        Assert.IsTrue(stack.IsDirty);

        stack.MarkSaved();
        Assert.IsFalse(stack.IsDirty);

        stack.Execute(Set(2));
        Assert.IsTrue(stack.IsDirty);

        stack.Undo();
        Assert.IsFalse(stack.IsDirty, "Undoing back to the save point is clean again.");

        stack.Undo();
        Assert.IsTrue(stack.IsDirty, "Undoing past the save point is dirty.");
    }

    [TestMethod]
    public void Clear_ResetsHistoryAndDirtyState()
    {
        var stack = new UndoStack();
        stack.Execute(Set(1));
        stack.Clear();
        Assert.IsFalse(stack.CanUndo);
        Assert.IsFalse(stack.IsDirty);
    }

    [TestMethod]
    public void PropertyChangeAction_DetectsNoOp()
    {
        _value = 3;
        Assert.IsTrue(Set(3).IsNoOp);
        Assert.IsFalse(Set(4).IsNoOp);
    }

    [TestMethod]
    public void Descriptions_FollowTheStacks()
    {
        var stack = new UndoStack();
        stack.Execute(Set(1));
        Assert.AreEqual("Set value", stack.UndoDescription);
        Assert.IsNull(stack.RedoDescription);
        stack.Undo();
        Assert.AreEqual("Set value", stack.RedoDescription);
    }
}
