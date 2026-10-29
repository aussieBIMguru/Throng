using System.Globalization;
using System.Windows.Media;
using Throng.Analysis;
using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Render.Gl;
using Throng.Render.Scene;
using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// Heatmap half of the main view model (M5): the MAP chips (Off / Density / LoS, D-201, and the RUN maps Max / E+ time /
/// Mean / Trail, D-216, D-217), painting the runner's density maps into per-layer textures for the viewport (D-202),
/// the legend, the warm-up note on run maps (D-218), the peak LoS ticker line (D-204), and the colour-blind ramp
/// setting (D-206).
/// </summary>
public sealed partial class MainViewModel
{
    private HeatmapMode _heatmapMode = HeatmapMode.Off;
    private float[] _densityValues = [];
    private long _densityVersion = -1;
    private SimRunner? _densityRunner;
    private readonly List<HeatLayerDraw> _heatDraws = [];
    private readonly Dictionary<int, HeatLayerDraw> _heatDrawCache = new();
    private string _heatKey = string.Empty;
    private long _heatOutputVersion;
    private string? _lastHeatTicker;
    private double _heatScale;
    private string _heatLegendNote = string.Empty;

    /// <summary>What the live heatmap shows (D-201); a view setting, not saved and not undoable.</summary>
    public HeatmapMode HeatmapMode
    {
        get => _heatmapMode;
        set
        {
            HeatmapMode old = _heatmapMode;
            if (SetProperty(ref _heatmapMode, value))
            {
                _lastHeatTicker = null;
                if (HeatmapPainter.MeasureOf(old) != HeatmapPainter.MeasureOf(value))
                {
                    // Another measure: fetch it afresh rather than repainting the old values.
                    _densityVersion = -1;
                    _heatKey = string.Empty;
                }

                _heatScale = 0;
                OnPropertyChanged(nameof(IsHeatOff));
                OnPropertyChanged(nameof(IsHeatDensity));
                OnPropertyChanged(nameof(IsHeatLos));
                OnPropertyChanged(nameof(IsHeatMax));
                OnPropertyChanged(nameof(IsHeatMean));
                OnPropertyChanged(nameof(IsHeatLosEOrWorse));
                OnPropertyChanged(nameof(IsHeatFootfall));
                RefreshHeatNote();
                OnHeatLegendChanged();
            }
        }
    }

    /// <summary>MAP ▸ OFF chip.</summary>
    public bool IsHeatOff
    {
        get => _heatmapMode == HeatmapMode.Off;
        set
        {
            // Chips always select; clicking the lit one keeps it lit.
            HeatmapMode = HeatmapMode.Off;
            OnPropertyChanged(nameof(IsHeatOff));
        }
    }

    /// <summary>MAP ▸ DENSITY chip.</summary>
    public bool IsHeatDensity
    {
        get => _heatmapMode == HeatmapMode.Density;
        set
        {
            HeatmapMode = HeatmapMode.Density;
            OnPropertyChanged(nameof(IsHeatDensity));
        }
    }

    /// <summary>MAP ▸ LOS chip.</summary>
    public bool IsHeatLos
    {
        get => _heatmapMode == HeatmapMode.Los;
        set
        {
            HeatmapMode = HeatmapMode.Los;
            OnPropertyChanged(nameof(IsHeatLos));
        }
    }

    /// <summary>MAP ▸ RUN ▸ MAX chip (D-217): highest density after the warm-up, on the LoS bands.</summary>
    public bool IsHeatMax
    {
        get => _heatmapMode == HeatmapMode.MaxDensity;
        set => SelectHeatChip(HeatmapMode.MaxDensity, nameof(IsHeatMax));
    }

    /// <summary>MAP ▸ RUN ▸ E+ TIME chip (D-217): time at LoS E or worse after the warm-up.</summary>
    public bool IsHeatLosEOrWorse
    {
        get => _heatmapMode == HeatmapMode.LosEOrWorse;
        set => SelectHeatChip(HeatmapMode.LosEOrWorse, nameof(IsHeatLosEOrWorse));
    }

    /// <summary>MAP ▸ RUN ▸ MEAN chip (D-217): mean density after the warm-up.</summary>
    public bool IsHeatMean
    {
        get => _heatmapMode == HeatmapMode.MeanDensity;
        set => SelectHeatChip(HeatmapMode.MeanDensity, nameof(IsHeatMean));
    }

    /// <summary>MAP ▸ RUN ▸ TRAIL chip (D-217): footfall after the warm-up.</summary>
    public bool IsHeatFootfall
    {
        get => _heatmapMode == HeatmapMode.Footfall;
        set => SelectHeatChip(HeatmapMode.Footfall, nameof(IsHeatFootfall));
    }

    /// <summary>Second line under the legend title on run maps: warm-up status and time gathered (D-218).</summary>
    public string HeatLegendNote
    {
        get => _heatLegendNote;
        private set
        {
            if (SetProperty(ref _heatLegendNote, value))
            {
                OnPropertyChanged(nameof(HasHeatLegendNote));
            }
        }
    }

    /// <summary>True when <see cref="HeatLegendNote"/> has text.</summary>
    public bool HasHeatLegendNote => _heatLegendNote.Length > 0;

    /// <summary>STUDY ▸ RUN: heatmaps in the colour-blind-safe ramp (D-206, undoable, saved with the study).</summary>
    public bool ColourBlindHeatmap
    {
        get => _study.Settings.ColourBlindHeatmap;
        set
        {
            Apply(value ? "Colour-blind-safe heatmap" : "Classic heatmap colours",
                () => _study.Settings.ColourBlindHeatmap, v => _study.Settings.ColourBlindHeatmap = v, value);
            OnHeatLegendChanged();
        }
    }

    /// <summary>True while a run is active and a heatmap mode is picked: the legend shows.</summary>
    public bool ShowHeatLegend => _isRunActive && _heatmapMode != HeatmapMode.Off;

    /// <summary>Legend title.</summary>
    public string HeatLegendTitle => _heatmapMode switch
    {
        HeatmapMode.Los => "LEVEL OF SERVICE (FRUIN)",
        HeatmapMode.MaxDensity => "PEAK LOS (FRUIN)",
        HeatmapMode.MeanDensity => "MEAN DENSITY",
        HeatmapMode.LosEOrWorse => "TIME AT LOS E OR WORSE",
        HeatmapMode.Footfall => "FOOTFALL",
        _ => "DENSITY",
    };

    /// <summary>Legend rows for the current mode and ramp.</summary>
    public IReadOnlyList<HeatLegendRow> HeatLegend =>
    [
        .. HeatmapPainter.Legend(_heatmapMode, HeatRamp, _heatScale).Select(e =>
        {
            var brush = new SolidColorBrush(Color.FromRgb(ToByte(e.R), ToByte(e.G), ToByte(e.B)));
            brush.Freeze();
            return new HeatLegendRow(brush, e.Label);
        }),
    ];

    private HeatmapRamp HeatRamp => _study.Settings.ColourBlindHeatmap ? HeatmapRamp.ColourBlindSafe : HeatmapRamp.Classic;

    /// <summary>
    /// Heat layers for the current view (D-202), repainted only when the map, the mode, the ramp or what's visible
    /// changed; null when no heatmap shows. Called by the viewport every frame on the UI thread.
    /// </summary>
    public IReadOnlyList<HeatLayerDraw>? FetchHeat(ViewMode mode, out long version)
    {
        version = -1;
        if (_runner is not { } runner || _heatmapMode == HeatmapMode.Off)
        {
            return null;
        }

        bool changed = RefreshDensity(runner);
        string key = HeatKey(mode);
        if (changed || key != _heatKey)
        {
            _heatKey = key;
            PaintHeat(runner.DensityLayout, mode);
            _heatOutputVersion++;
        }

        version = _heatOutputVersion;
        return _heatDraws;
    }

    /// <summary>Copies the runner's latest map when it has changed (a new run starts a fresh copy).</summary>
    private bool RefreshDensity(SimRunner runner)
    {
        if (!ReferenceEquals(runner, _densityRunner))
        {
            _densityRunner = runner;
            _densityVersion = -1;
            _heatDrawCache.Clear();
            _heatKey = string.Empty;
        }

        return runner.CopyDensity(ref _densityValues, ref _densityVersion, HeatmapPainter.MeasureOf(_heatmapMode));
    }

    /// <summary>What's visible: mode, ramp, view, active level and visible levels.</summary>
    private string HeatKey(ViewMode mode)
    {
        var key = new System.Text.StringBuilder();
        key.Append(_heatmapMode).Append('|').Append(HeatRamp).Append('|').Append(mode).Append('|');
        if (mode == ViewMode.Plan)
        {
            key.Append(_activeLevel?.Id);
        }
        else
        {
            foreach (Level level in _study.Levels)
            {
                key.Append(level.IsVisible ? '1' : '0');
            }
        }

        return key.ToString();
    }

    private void PaintHeat(DensityGrid layout, ViewMode mode)
    {
        _heatDraws.Clear();
        if (_densityValues.Length != layout.CellCount)
        {
            return;
        }

        // Scaled run maps share one scale over every level, so levels compare; the legend follows it.
        double scale = HeatmapPainter.ScaleFor(_heatmapMode, layout.Kinds, _densityValues);
        if (scale != _heatScale)
        {
            _heatScale = scale;
            OnPropertyChanged(nameof(HeatLegend));
        }

        Guid? active = _activeLevel?.Id;
        IReadOnlyList<DensityLayer> layers = layout.Layers;
        for (int l = 0; l < layers.Count; l++)
        {
            DensityLayer layer = layers[l];
            bool show = mode == ViewMode.Plan
                ? layer.LevelId == active
                : _study.FindLevel(layer.LevelId)?.IsVisible ?? true;
            if (!show)
            {
                continue;
            }

            if (!_heatDrawCache.TryGetValue(l, out HeatLayerDraw? draw))
            {
                draw = new HeatLayerDraw(l, layer.Corners(), layer.Nx, layer.Ny, new byte[layer.CellCount * 4]);
                _heatDrawCache[l] = draw;
            }

            HeatmapPainter.PaintLayer(layer, layout.Kinds, _densityValues, _heatmapMode, HeatRamp, draw.Bgra, _heatScale);
            _heatDraws.Add(draw);
        }
    }

    /// <summary>
    /// Ticker line for the worst place on the map (D-204) when it reaches LoS E or worse; set only when the line
    /// changes, so other ticker messages aren't overwritten every refresh.
    /// </summary>
    private void RefreshHeatTicker()
    {
        RefreshHeatNote();
        if (_runner is not { } runner || _heatmapMode is not (HeatmapMode.Density or HeatmapMode.Los))
        {
            return;
        }

        RefreshDensity(runner);
        DensityGrid layout = runner.DensityLayout;
        if (_densityValues.Length != layout.CellCount
            || HeatmapPainter.FindPeak(layout.Layers, layout.Kinds, _densityValues) is not { } peak
            || peak.Los < LevelOfService.E)
        {
            return;
        }

        string line = string.Create(CultureInfo.InvariantCulture,
            $"PEAK ▸ LoS {peak.Los} at {PlaceName(runner, layout.Layers[peak.Layer], peak.Cell)} ({peak.Density:0.0} p/m²)");
        if (line != _lastHeatTicker)
        {
            _lastHeatTicker = line;
            Ticker = line;
        }
    }

    /// <summary>
    /// A name for where a map cell is: the connector for flights and landings, else the nearest device within 6 m on
    /// that floor, else the level.
    /// </summary>
    private string PlaceName(SimRunner runner, DensityLayer layer, int cell)
    {
        if (!layer.IsFloor && layer.Label.Length > 0)
        {
            return layer.Label;
        }

        Point2 at = layer.CellCentre(cell);
        string? best = null;
        double bestDistance = 6.0;
        foreach (SimDevice device in runner.Model.Devices)
        {
            double distance = Point2.Distance(device.Centre, at);
            if (device.Floor == layer.Surface && distance < bestDistance)
            {
                bestDistance = distance;
                best = device.Name;
            }
        }

        return best ?? _study.FindLevel(layer.LevelId)?.Name ?? "the plan";
    }

    /// <summary>Warm-up status on run maps (D-218): counting starts at the warm-up, then time gathered.</summary>
    private void RefreshHeatNote()
    {
        if (_runner is not { } runner || !HeatmapPainter.IsCumulative(_heatmapMode))
        {
            HeatLegendNote = string.Empty;
            return;
        }

        double gathered = runner.DensityAccumulatedS;
        double warmUp = runner.DensityLayout.WarmUpS;
        HeatLegendNote = gathered <= 0 && warmUp > 0
            ? string.Create(CultureInfo.InvariantCulture, $"WARM-UP ▸ COUNTS FROM {Clock(warmUp)}")
            : warmUp > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{Clock(gathered)} AFTER WARM-UP")
                : string.Create(CultureInfo.InvariantCulture, $"{Clock(gathered)} GATHERED");
    }

    /// <summary>m:ss, or h:mm:ss past an hour.</summary>
    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}:{t.Seconds:00}");
    }

    /// <summary>Chips always select; clicking the lit one keeps it lit.</summary>
    private void SelectHeatChip(HeatmapMode mode, string chipProperty)
    {
        HeatmapMode = mode;
        OnPropertyChanged(chipProperty);
    }

    private void OnHeatLegendChanged()
    {
        OnPropertyChanged(nameof(ShowHeatLegend));
        OnPropertyChanged(nameof(HeatLegendTitle));
        OnPropertyChanged(nameof(HeatLegend));
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255f), 0, 255);
}
