namespace Throng.App.ViewModels;

/// <summary>One candidate device or zone in SELECTION ▸ node ▸ RUN (round 019, D-336).</summary>
/// <param name="Name">The device's or zone's name.</param>
/// <param name="CountText">People who headed for it, e.g. "124".</param>
/// <param name="ShareText">Its share of the node's people, e.g. "38%".</param>
/// <param name="WaitText">Devices: mean wait to be served, e.g. "18 s" ("–" when no one was served); empty for zones.</param>
public sealed record JourneyRunRow(string Name, string CountText, string ShareText, string WaitText);
