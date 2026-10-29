using System.Windows.Media;

namespace Throng.App.Views;

/// <summary>One line on the time-series graph (round 011c, D-220).</summary>
/// <param name="Label">Legend text.</param>
/// <param name="Colour">Line colour.</param>
/// <param name="Values">One value per sample time.</param>
public sealed record GraphLine(string Label, Color Colour, double[] Values);
