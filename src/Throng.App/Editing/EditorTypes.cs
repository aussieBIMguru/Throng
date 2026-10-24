using Throng.Core.Editing;
using Throng.Core.Model;

namespace Throng.App.Editing;

/// <summary>Plan editing tools (M1, plan view only per D-026).</summary>
public enum EditorTool
{
    /// <summary>Pick, box-select, move and reshape regions.</summary>
    Select,

    /// <summary>Two-corner axis-aligned rectangle.</summary>
    Rectangle,

    /// <summary>Click-by-click closed polygon.</summary>
    Polygon,

    /// <summary>Centre + radius circle, stored as a polygon (D-036).</summary>
    Circle,

    /// <summary>Open polyline with thickness.</summary>
    Wall,
}

/// <summary>What the editor needs from its owner. Implemented by the main view model.</summary>
public interface IEditorHost
{
    /// <summary>The open study.</summary>
    Study Study { get; }

    /// <summary>Level being edited, if any.</summary>
    Level? ActiveLevelModel { get; }

    /// <summary>Runs an action through the undo stack.</summary>
    void Execute(IUndoableAction action);

    /// <summary>Callback actions invoke after every do/undo so views refresh.</summary>
    void NotifyModelChanged();

    /// <summary>Shows a short status message in the ticker.</summary>
    void ShowMessage(string message);
}
