using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Throng.Core.Library;

namespace Throng.App.ViewModels;

/// <summary>
/// One distribution in a definition editor (round 017, D-289): its type, the numbers that type uses, and the
/// distribution itself for the small plot. Every change builds a new, consistent distribution
/// (<see cref="DistributionShape.Normalised"/>) and hands it to the owner, which applies it through the undo stack.
/// </summary>
public sealed class DistributionEditorViewModel : ObservableObject
{
    private static readonly IReadOnlyList<LabelledOption<DistributionKind>> Kinds =
    [
        new(DistributionKind.Fixed, "Fixed"),
        new(DistributionKind.Uniform, "Uniform (min–max)"),
        new(DistributionKind.Triangular, "Triangular (min, likely, max)"),
        new(DistributionKind.NormalClamped, "Normal (mean ± sd, clamped)"),
        new(DistributionKind.Exponential, "Exponential (mean)"),
    ];

    private readonly Func<Distribution> _read;
    private readonly Action<Distribution> _write;

    /// <summary>Wraps a distribution read and written through the owner.</summary>
    /// <param name="label">Field label, e.g. "Service time".</param>
    /// <param name="unit">Unit shown after the numbers, e.g. "s" or "m/s".</param>
    /// <param name="read">Reads the current distribution from the definition.</param>
    /// <param name="write">Applies a new distribution (undoable).</param>
    public DistributionEditorViewModel(string label, string unit, Func<Distribution> read, Action<Distribution> write)
    {
        Label = label;
        Unit = unit;
        _read = read;
        _write = write;
    }

    /// <summary>Field label.</summary>
    public string Label { get; }

    /// <summary>Unit of the numbers.</summary>
    public string Unit { get; }

    /// <summary>The distribution as it is now (the plot binds to it).</summary>
    public Distribution Current => _read() ?? Distribution.Fixed(0);

    /// <summary>The type choices.</summary>
    public IReadOnlyList<LabelledOption<DistributionKind>> KindOptions => Kinds;

    /// <summary>The type; changing it keeps the centre and spread roughly (<see cref="DistributionShape.Converted"/>).</summary>
    public DistributionKind Kind
    {
        get => Current.Kind;
        set
        {
            if (value != Current.Kind)
            {
                _write(DistributionShape.Converted(Current, value));
            }
        }
    }

    /// <summary>Shown for Fixed.</summary>
    public bool HasValue => Current.Kind == DistributionKind.Fixed;

    /// <summary>Shown for Uniform, Triangular and Normal.</summary>
    public bool HasBounds => Current.Kind is DistributionKind.Uniform or DistributionKind.Triangular or DistributionKind.NormalClamped;

    /// <summary>Shown for Triangular.</summary>
    public bool HasMode => Current.Kind == DistributionKind.Triangular;

    /// <summary>Shown for Normal and Exponential.</summary>
    public bool HasMean => Current.Kind is DistributionKind.NormalClamped or DistributionKind.Exponential;

    /// <summary>Shown for Exponential (Normal shows its mean beside the bounds).</summary>
    public bool HasMeanOnly => Current.Kind == DistributionKind.Exponential;

    /// <summary>Shown for Normal.</summary>
    public bool HasStdDev => Current.Kind == DistributionKind.NormalClamped;

    /// <summary>The constant (Fixed).</summary>
    public double Value
    {
        get => Current.Value;
        set => Change(d => d.Value = value);
    }

    /// <summary>Lower bound.</summary>
    public double Min
    {
        get => Current.Min;
        set => Change(d => d.Min = value);
    }

    /// <summary>Upper bound.</summary>
    public double Max
    {
        get => Current.Max;
        set => Change(d => d.Max = value);
    }

    /// <summary>Most likely value (Triangular).</summary>
    public double Mode
    {
        get => Current.Mode;
        set => Change(d => d.Mode = value);
    }

    /// <summary>Mean (Normal, Exponential).</summary>
    public double Mean
    {
        get => Current.Mean;
        set => Change(d => d.Mean = value);
    }

    /// <summary>Standard deviation (Normal).</summary>
    public double StdDev
    {
        get => Current.StdDev;
        set => Change(d => d.StdDev = value);
    }

    /// <summary>One line under the plot: the shape and its average.</summary>
    public string SummaryText => string.Create(
        CultureInfo.InvariantCulture,
        $"{Current.Describe()} {Unit} · average {Current.ExpectedValue:0.##} {Unit}");

    /// <summary>Re-reads everything (after an edit or undo).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private void Change(Action<Distribution> edit)
    {
        Distribution current = Current;
        var next = new Distribution
        {
            Kind = current.Kind,
            Value = current.Value,
            Min = current.Min,
            Max = current.Max,
            Mode = current.Mode,
            Mean = current.Mean,
            StdDev = current.StdDev,
        };
        edit(next);
        _write(DistributionShape.Normalised(next));
    }
}
