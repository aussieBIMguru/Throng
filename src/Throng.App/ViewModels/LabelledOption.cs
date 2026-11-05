namespace Throng.App.ViewModels;

/// <summary>A choice in a combo box with a display label.</summary>
/// <typeparam name="T">Value type.</typeparam>
/// <param name="Value">The value stored.</param>
/// <param name="Label">What the combo shows.</param>
public sealed record LabelledOption<T>(T Value, string Label);
