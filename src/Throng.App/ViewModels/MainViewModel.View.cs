using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Model;
using Throng.Render.Cameras;
using Throng.Render.Gl;
using Throng.Render.Scene;

namespace Throng.App.ViewModels;

/// <summary>
/// M6 views (round 015): the ISO view and its quarter-turn presets (D-263), and STUDY ▸ VIEW: clip above a level, a
/// section box and exploded levels (D-264–D-266, D-268). Session only (D-265): nothing here is saved or undoable, and
/// it all works while a run is going. Plan view ignores it.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Most extra height between levels the explode slider gives, metres.</summary>
    public const double MaxExplodeM = 30;

    /// <summary>Smallest section box width per axis, percent of the extent.</summary>
    private const double MinBoxPct = 2;

    /// <summary>How far under the floor above a level the clip cuts, metres (so that floor's fill and outline go).</summary>
    private const double ClipUnderFloorM = 0.05;

    private int _isoPreset;
    private bool _clipEnabled;
    private Guid? _clipLevelId;
    private bool _sectionBox;
    private double _boxWest, _boxEast = 100, _boxSouth, _boxNorth = 100, _boxBottom, _boxTop = 100;
    private double _explodeM;
    private RelayCommand? _isoViewCommand;
    private RelayCommand? _clearViewCommand;

    /// <summary>ISO view (F4, D-263, D-268).</summary>
    public RelayCommand IsoViewCommand => _isoViewCommand ??= new RelayCommand(() => ViewMode = ViewMode.Iso);

    /// <summary>VIEW ▸ CLEAR: no clip, no section box, no explode.</summary>
    public RelayCommand ClearViewCommand => _clearViewCommand ??= new RelayCommand(ClearView);

    /// <summary>True in ISO view (toolbar highlight).</summary>
    public bool IsIsoView => _viewMode == ViewMode.Iso;

    /// <summary>True in 3D or ISO, where the VIEW controls apply.</summary>
    public bool IsViewControlsEnabled => _viewMode != ViewMode.Plan;

    /// <summary>ISO preset 0–3 (SW, SE, NE, NW, by where the eye sits).</summary>
    public int IsoPreset
    {
        get => _isoPreset;
        private set
        {
            if (SetProperty(ref _isoPreset, ((value % 4) + 4) % 4))
            {
                OnPropertyChanged(nameof(HudModeText));
            }
        }
    }

    /// <summary>Turns the ISO view a quarter (+1 with ], −1 with [).</summary>
    public void RotateIso(int step)
    {
        IsoPreset += step;
        Ticker = $"ISO ▸ looking from the {OrbitCamera.IsoPresetNames[IsoPreset]} ▸ [ and ] turn it";
    }

    /// <summary>VIEW ▸ clip above a level on/off (D-266); turning it on picks the active level the first time.</summary>
    public bool ClipEnabled
    {
        get => _clipEnabled;
        set
        {
            if (value && _clipLevelId is null)
            {
                _clipLevelId = ActiveLevel?.Id;
                OnPropertyChanged(nameof(ClipLevel));
            }

            if (SetProperty(ref _clipEnabled, value))
            {
                ViewSettingsChanged();
            }
        }
    }

    /// <summary>VIEW ▸ the level everything above is cut away from.</summary>
    public LevelViewModel? ClipLevel
    {
        get => _clipLevelId is { } id ? Levels.FirstOrDefault(l => l.Id == id) : null;
        set
        {
            // The list clears its selection while levels are rebuilt; keep the level by id through that.
            if (value is null || value.Id == _clipLevelId)
            {
                return;
            }

            _clipLevelId = value.Id;
            OnPropertyChanged();
            ViewSettingsChanged();
        }
    }

    /// <summary>VIEW ▸ section box on/off (D-266).</summary>
    public bool SectionBoxEnabled
    {
        get => _sectionBox;
        set
        {
            if (SetProperty(ref _sectionBox, value))
            {
                ViewSettingsChanged();
            }
        }
    }

    /// <summary>Section box west edge, percent of the study's X extent.</summary>
    public double BoxWest { get => _boxWest; set => SetBox(ref _boxWest, Math.Min(value, _boxEast - MinBoxPct)); }

    /// <summary>Section box east edge, percent.</summary>
    public double BoxEast { get => _boxEast; set => SetBox(ref _boxEast, Math.Max(value, _boxWest + MinBoxPct)); }

    /// <summary>Section box south edge, percent of the study's Y extent.</summary>
    public double BoxSouth { get => _boxSouth; set => SetBox(ref _boxSouth, Math.Min(value, _boxNorth - MinBoxPct)); }

    /// <summary>Section box north edge, percent.</summary>
    public double BoxNorth { get => _boxNorth; set => SetBox(ref _boxNorth, Math.Max(value, _boxSouth + MinBoxPct)); }

    /// <summary>Section box bottom, percent of the study's height range.</summary>
    public double BoxBottom { get => _boxBottom; set => SetBox(ref _boxBottom, Math.Min(value, _boxTop - MinBoxPct)); }

    /// <summary>Section box top, percent.</summary>
    public double BoxTop { get => _boxTop; set => SetBox(ref _boxTop, Math.Max(value, _boxBottom + MinBoxPct)); }

    /// <summary>VIEW ▸ explode: extra height between consecutive levels, metres (D-264).</summary>
    public double ExplodeM
    {
        get => _explodeM;
        set
        {
            double v = Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, MaxExplodeM) * 2, MidpointRounding.AwayFromZero) / 2;
            if (SetProperty(ref _explodeM, v))
            {
                OnPropertyChanged(nameof(ExplodeText));
                ViewSettingsChanged();
            }
        }
    }

    /// <summary>The explode amount for the label beside the slider.</summary>
    public string ExplodeText => _explodeM <= 0 ? "OFF" : string.Create(CultureInfo.InvariantCulture, $"+{_explodeM:0.0} m");

    /// <summary>What the viewport draws in 3D and ISO (rebuilt on every read, so the clip follows level edits).</summary>
    public ViewSettings ViewSettings => new()
    {
        ExplodeGapM = _explodeM,
        ClipAboveZ = _clipEnabled ? ClipAboveZ() : null,
        SectionBox = _sectionBox,
        BoxWest = _boxWest / 100,
        BoxEast = _boxEast / 100,
        BoxSouth = _boxSouth / 100,
        BoxNorth = _boxNorth / 100,
        BoxBottom = _boxBottom / 100,
        BoxTop = _boxTop / 100,
    };

    /// <summary>
    /// The clip height for the chosen level: just under the next floor up, or for the top level just over its storey
    /// height; null when the level is gone.
    /// </summary>
    private double? ClipAboveZ()
    {
        if (_clipLevelId is not { } id || _study.FindLevel(id) is not { } level)
        {
            return null;
        }

        double? next = null;
        foreach (Level other in _study.Levels)
        {
            if (other.ElevationM > level.ElevationM + 1e-3 && (next is null || other.ElevationM < next))
            {
                next = other.ElevationM;
            }
        }

        return next is { } z ? z - ClipUnderFloorM : level.ElevationM + level.HeightM + ClipUnderFloorM;
    }

    /// <summary>A section box handle was dragged in 3D (D-277): move that side's slider (0–5: W, E, S, N, bottom, top).</summary>
    public void DragBoxFace(int face, double share)
    {
        double pct = Math.Clamp(share, 0, 1) * 100;
        switch (face)
        {
            case 0: BoxWest = pct; break;
            case 1: BoxEast = pct; break;
            case 2: BoxSouth = pct; break;
            case 3: BoxNorth = pct; break;
            case 4: BoxBottom = pct; break;
            case 5: BoxTop = pct; break;
        }
    }

    private void SetBox(ref double field, double value)
    {
        double v = Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 100), 1);
        if (Math.Abs(field - v) < 1e-9)
        {
            return;
        }

        field = v;
        OnPropertyChanged(nameof(BoxWest));
        OnPropertyChanged(nameof(BoxEast));
        OnPropertyChanged(nameof(BoxSouth));
        OnPropertyChanged(nameof(BoxNorth));
        OnPropertyChanged(nameof(BoxBottom));
        OnPropertyChanged(nameof(BoxTop));
        ViewSettingsChanged();
    }

    private void ClearView()
    {
        _clipEnabled = false;
        _sectionBox = false;
        _boxWest = _boxSouth = _boxBottom = 0;
        _boxEast = _boxNorth = _boxTop = 100;
        _explodeM = 0;
        OnPropertyChanged(nameof(ClipEnabled));
        OnPropertyChanged(nameof(SectionBoxEnabled));
        OnPropertyChanged(nameof(BoxWest));
        OnPropertyChanged(nameof(BoxEast));
        OnPropertyChanged(nameof(BoxSouth));
        OnPropertyChanged(nameof(BoxNorth));
        OnPropertyChanged(nameof(BoxBottom));
        OnPropertyChanged(nameof(BoxTop));
        OnPropertyChanged(nameof(ExplodeM));
        OnPropertyChanged(nameof(ExplodeText));
        ViewSettingsChanged();
        Ticker = "VIEW ▸ cleared: no cutaway, levels at true elevation.";
    }
}
