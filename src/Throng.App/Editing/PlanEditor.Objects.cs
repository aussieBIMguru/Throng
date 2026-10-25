using System.Globalization;
using Throng.Core.Common;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.Editing;

/// <summary>
/// Round 005 tools of the plan editor: PLACE for devices and travelators (D-089, D-090) and CALIBRATE for
/// underlay scale by reference distance (D-084); round 006 adds drops from LIBRARY (D-098).
/// </summary>
public sealed partial class PlanEditor
{
    private const double DefaultTravelatorM = 20;

    /// <summary>Facing of a device dropped from LIBRARY (D-098): north, like a fresh PLACE.</summary>
    private const double DefaultDropFacingDeg = 90;

    private TemplateBase? _placePreset;
    private double _placeAngle = 90;
    private double _placeLength = DefaultTravelatorM;
    private Device? _placeDevice;
    private ConnectorPreview? _placeTravelator;
    private string? _placeText;
    private Guid? _calibrateId;

    /// <summary>Raised when the editor wants the typed-dimension box opened (after the second calibration click).</summary>
    public event EventHandler<string>? TypedInputRequested;

    /// <summary>Preset placed by PLACE: a point device or a travelator. Zone devices and other kinds are ignored.</summary>
    public TemplateBase? PlacePreset
    {
        get => _placePreset;
        set
        {
            _placePreset = value switch
            {
                DeviceTemplate { Kind: DeviceKind.Point } d => d,
                ConnectorTemplate { Kind: ConnectorKind.Travelator } t => t,
                _ => null,
            };
            if (_placePreset is ConnectorTemplate)
            {
                _placeAngle = 0;
            }

            RefreshPlacePreview();
        }
    }

    /// <summary>True when PLACE is placing a travelator (the second click sets its length too).</summary>
    public bool IsPlacingTravelator => _placePreset is ConnectorTemplate;

    /// <summary>Device previewed at the cursor while placing, or null.</summary>
    public Device? PlaceDevicePreview => _tool == EditorTool.Place ? _placeDevice : null;

    /// <summary>Travelator previewed at the cursor while placing, or null.</summary>
    public ConnectorPreview? PlaceTravelatorPreview => _tool == EditorTool.Place ? _placeTravelator : null;

    /// <summary>Readout for the PLACE preview.</summary>
    public string? PlacePreviewText => _tool == EditorTool.Place ? _placeText : null;

    /// <summary>The two calibration points (the second follows the cursor until clicked), or null.</summary>
    public (Point2 A, Point2 B)? CalibrationLine =>
        _tool == EditorTool.Calibrate && _points.Count >= 1
            ? (_points[0], _points.Count >= 2 ? _points[1] : _snap.Point)
            : null;

    /// <summary>Selects one object by id (from a panel list); dropped again if it isn't selectable on the active level.</summary>
    public void Select(Guid id)
    {
        if (CheckEditable())
        {
            Tool = EditorTool.Select;
            SetSelection([id]);
            Revalidate();
        }
    }

    /// <summary>Starts calibrating an underlay (from the SELECTION panel's SET SCALE button).</summary>
    public void StartCalibration(Guid underlayId)
    {
        if (!CheckEditable() || _host.Study.FindUnderlay(underlayId) is not { } underlay)
        {
            return;
        }

        if (underlay.IsLocked)
        {
            RaiseFlash("UNDERLAY LOCKED ▸ UNLOCK IT TO SET ITS SCALE");
            return;
        }

        Tool = EditorTool.Calibrate;
        _calibrateId = underlayId;
        SetSelection([underlayId]);
        _host.ShowMessage("SET SCALE ▸ click two points on the image a known distance apart, then type that distance in metres.");
    }

    // ------------------------------------------------------------------ PLACE (D-089, D-090)

    private bool CheckPlaceReady()
    {
        if (_placePreset is null)
        {
            RaiseFlash("PICK A DEVICE OR TRAVELATOR PRESET");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Drag-and-drop from LIBRARY (D-098), with the tool and preset already picked: a device is placed at the drop
    /// point facing the default direction (rotate it with its grip after); a travelator or a stair, escalator or ramp
    /// takes the drop point as its first click, then is aimed as usual.
    /// </summary>
    public void DropAt(Point2 point, double metresPerDip)
    {
        _metresPerDip = metresPerDip;
        if (!CheckEditable())
        {
            return;
        }

        _points.Clear();
        switch (_tool)
        {
            case EditorTool.Place when _placePreset is DeviceTemplate:
                _placeAngle = DefaultDropFacingDeg;
                PlaceDown(point, shift: false);
                CommitPlace();
                break;

            case EditorTool.Place:
                PlaceDown(point, shift: false);
                break;

            case EditorTool.Connect:
                ConnectDown(point, shift: false);
                break;
        }

        RaiseOverlay();
    }

    private void PlaceDown(Point2 raw, bool shift)
    {
        if (!CheckPlaceReady())
        {
            return;
        }

        if (_points.Count == 0)
        {
            _points.Add(SnapDrawPoint(raw, shift).Point);
            UpdatePlaceAim(raw, shift);
            _host.ShowMessage(IsPlacingTravelator
                ? "PLACE ▸ click the far end of the belt (or type its length)."
                : "PLACE ▸ aim the service side and click (or type an angle). 15° steps, Shift for free.");
            return;
        }

        CommitPlace();
    }

    /// <summary>Aim (15° steps unless Shift) and, for travelators, length from the first point; then refresh the preview.</summary>
    private void UpdatePlaceAim(Point2 cursor, bool shift)
    {
        if (_tool != EditorTool.Place)
        {
            return;
        }

        if (_points.Count == 1)
        {
            Point2 toward = cursor - _points[0];
            if (toward.Length > 2 * _metresPerDip)
            {
                double angle = ConnectorFactory.AngleOf(toward);
                _placeAngle = shift ? angle : ConnectorFactory.SnapAngle(angle, AngleStepDeg);
                if (IsPlacingTravelator)
                {
                    _placeLength = Math.Max(1, Math.Round(toward.Length, 2));
                }
            }
        }

        BuildPlacePreview(_points.Count == 1 ? _points[0] : cursor);
    }

    private void RefreshPlacePreview()
    {
        if (_tool == EditorTool.Place && _hasCursor)
        {
            BuildPlacePreview(_points.Count == 1 ? _points[0] : _snap.Point);
        }
        else
        {
            ClearPlacePreview();
        }

        RaiseOverlay();
    }

    private void ClearPlacePreview()
    {
        _placeDevice = null;
        _placeTravelator = null;
        _placeText = null;
    }

    private void BuildPlacePreview(Point2 at)
    {
        ClearPlacePreview();
        if (_host.ActiveLevelModel is not { } level)
        {
            return;
        }

        CultureInfo c = CultureInfo.InvariantCulture;
        switch (_placePreset)
        {
            case DeviceTemplate device:
                _placeDevice = DeviceFactory.FromTemplate(_host.Study, device, level.Id, at, _placeAngle);
                _placeText = string.Create(c, $"{device.Name} · {device.WidthM:0.0#} × {device.DepthM:0.0#} m · faces {_placeAngle:0.#}°");
                break;

            case ConnectorTemplate travelator:
                Connector belt = ConnectorFactory.FromTemplate(_host.Study, travelator, level.Id, level.Id, at, _placeAngle, TurnSide.Left, _placeLength)[0];
                if (ConnectorLayout.Compute(belt, _host.Study) is { IsSuccess: true, Value: { } layout })
                {
                    _placeTravelator = new ConnectorPreview(belt, layout);
                }

                _placeText = string.Create(c, $"{travelator.Name} · {_placeLength:0.0} m · {_placeAngle:0.#}°");
                break;

            default:
                _placeText = "Pick a device in the DRAW strip, or double-click one in LIBRARY ▸ DEVICES.";
                break;
        }
    }

    private bool ApplyPlaceTyped(double[] v)
    {
        if (_points.Count != 1)
        {
            return false;
        }

        if (v.Length == 2)
        {
            var direction = new Point2(v[0], v[1]);
            if (direction.Length < 1e-9)
            {
                RaiseFlash("TYPE AN ANGLE OR A DIRECTION");
                return false;
            }

            _placeAngle = ConnectorFactory.AngleOf(direction);
            if (IsPlacingTravelator)
            {
                _placeLength = Math.Max(1, direction.Length);
            }
        }
        else if (IsPlacingTravelator)
        {
            if (!(v[0] >= 1))
            {
                RaiseFlash("TYPE A LENGTH OF AT LEAST 1 M");
                return false;
            }

            _placeLength = v[0];
        }
        else
        {
            _placeAngle = ConnectorFactory.NormaliseAngle(v[0]);
        }

        return CommitPlace();
    }

    private bool CommitPlace()
    {
        if (!CheckPlaceReady() || _points.Count != 1 || _host.ActiveLevelModel is not { } level)
        {
            return false;
        }

        Study study = _host.Study;
        TemplateBase embedded = study.Library.AddCopy(_placePreset!, TemplateSource.Embedded);
        switch (_placePreset)
        {
            case DeviceTemplate template:
            {
                Device device = DeviceFactory.FromTemplate(study, template, level.Id, _points[0], _placeAngle);
                device.TemplateId = embedded.Id;
                _host.Execute(new AddItemsAction<Device>($"Place {device.Name}", study.Devices, [device], _host.NotifyModelChanged));
                SetSelection([device.Id]);
                break;
            }

            case ConnectorTemplate template:
            {
                Connector belt = ConnectorFactory.FromTemplate(study, template, level.Id, level.Id, _points[0], _placeAngle, TurnSide.Left, _placeLength)[0];
                belt.TemplateId = embedded.Id;
                _host.Execute(new AddItemsAction<Connector>($"Place {belt.Name}", study.Connectors, [belt], _host.NotifyModelChanged));
                SetSelection([belt.Id]);
                break;
            }

            default:
                return false;
        }

        _points.Clear();
        RefreshPlacePreview();
        return true;
    }

    // ------------------------------------------------------------------ CALIBRATE (D-084)

    private void CalibrateDown(Point2 raw, bool shift)
    {
        if (_calibrateId is not { } id || _host.Study.FindUnderlay(id) is null)
        {
            RaiseFlash("SELECT AN UNDERLAY AND PRESS SET SCALE");
            Tool = EditorTool.Select;
            return;
        }

        // Shift keeps the second point square to the first; otherwise points are exactly where you click.
        Point2 p = shift && _points.Count == 1 ? Ortho(_points[0], raw) : raw;
        if (_points.Count >= 2)
        {
            _points.Clear();
        }

        _points.Add(p);
        if (_points.Count == 2)
        {
            string measured = string.Create(CultureInfo.InvariantCulture, $"{Point2.Distance(_points[0], _points[1]):0.###}");
            _host.ShowMessage($"SET SCALE ▸ now {measured} m on the image. Type the real distance in metres and press Enter (Esc cancels).");
            TypedInputRequested?.Invoke(this, "REAL DISTANCE  m");
        }
    }

    private bool ApplyCalibrationDistance(double[] v)
    {
        if (_points.Count < 2 || _calibrateId is not { } id || _host.Study.FindUnderlay(id) is not { } underlay)
        {
            RaiseFlash("CLICK TWO POINTS ON THE IMAGE FIRST");
            return false;
        }

        if (v.Length != 1 || !(v[0] > 0))
        {
            RaiseFlash("TYPE THE REAL DISTANCE IN METRES");
            return false;
        }

        Underlay? next = UnderlayGeometry.Calibrated(underlay, _points[0], _points[1], v[0]);
        if (next is null)
        {
            RaiseFlash("PICK TWO DIFFERENT POINTS");
            return false;
        }

        string description = string.Create(CultureInfo.InvariantCulture, $"Set scale of {underlay.Name} ({next.MetresPerPixel * 1000:0.##} mm/px)");
        _host.Execute(StateActions.Underlays(description, [(underlay, next)], _host.NotifyModelChanged));
        _points.Clear();
        _calibrateId = null;
        Tool = EditorTool.Select;
        SetSelection([underlay.Id]);
        return true;
    }

    private static Point2 Ortho(Point2 origin, Point2 p)
    {
        Point2 d = p - origin;
        return Math.Abs(d.X) >= Math.Abs(d.Y) ? new Point2(p.X, origin.Y) : new Point2(origin.X, p.Y);
    }
}
