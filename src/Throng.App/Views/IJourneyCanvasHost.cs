using Throng.Core.Library;

namespace Throng.App.Views;

/// <summary>One choice in a node chip's dropdown (D-323).</summary>
/// <param name="Label">Menu text.</param>
/// <param name="Value">What the host applies when it's picked.</param>
/// <param name="IsCurrent">True for the node's current setting (ticked).</param>
public sealed record JourneyChipOption(string Label, object Value, bool IsCurrent);

/// <summary>
/// What the JOURNEY canvas (D-308, D-323–D-327) asks of whoever owns the journey: the edits, as undoable steps, and
/// the text and choices of the dropdown chips drawn on the nodes. The canvas only draws and hit-tests.
/// </summary>
public interface IJourneyCanvasHost
{
    /// <summary>False while a run is on (the canvas still selects and pans).</summary>
    bool CanEdit { get; }

    /// <summary>The chips drawn on a node, top to bottom (e.g. "Gate ▾", "Quickest ▾").</summary>
    IReadOnlyList<string> ChipTexts(JourneyNode node);

    /// <summary>The choices for chip <paramref name="chip"/> of a node.</summary>
    IReadOnlyList<JourneyChipOption> ChipOptions(Guid nodeId, int chip);

    /// <summary>Applies a chip choice.</summary>
    void ChooseChip(Guid nodeId, int chip, JourneyChipOption option);

    /// <summary>Moves a node (one undo step per drag).</summary>
    void MoveNode(Guid id, double x, double y);

    /// <summary>Wires an output into a node; refusals go to the ticker.</summary>
    void Connect(Guid from, int port, Guid to);

    /// <summary>Removes the wire on an output.</summary>
    void Disconnect(Guid from, int port);

    /// <summary>Deletes a node (never Spawn) and its wires.</summary>
    void RemoveNode(Guid id);

    /// <summary>Adds a node at a canvas point, optionally wired from an output; returns the new node's id.</summary>
    Guid? AddNode(JourneyNodeKind kind, double x, double y, Guid? wireFrom, int wirePort);

    /// <summary>The run overlay (round 019, D-331–D-335); <see cref="JourneyOverlay.Empty"/> when there's nothing.</summary>
    JourneyOverlay Overlay { get; }

    /// <summary>The strip's ACTUAL / PREDICTED chips (D-334).</summary>
    void SetOverlayPredicted(bool predicted);

    /// <summary>The profile filter's choices (D-333): "All profiles", then the profiles on the journey's mix lines.</summary>
    IReadOnlyList<JourneyChipOption> OverlayProfileOptions();

    /// <summary>Applies a profile filter choice.</summary>
    void ChooseOverlayProfile(JourneyChipOption option);
}
