using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>One tick in a level checklist ("on this level", "stops here", "exit here", D-088).</summary>
public sealed class LevelToggleViewModel : ObservableObject
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    /// <summary>Creates a toggle over a getter and an undoable setter.</summary>
    public LevelToggleViewModel(string name, Func<bool> get, Action<bool> set)
    {
        Name = name;
        _get = get;
        _set = set;
    }

    /// <summary>Level name.</summary>
    public string Name { get; }

    /// <summary>Ticked state.</summary>
    public bool IsOn
    {
        get => _get();
        set
        {
            if (value != _get())
            {
                _set(value);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>A new list with <paramref name="id"/> added or removed (lists are replaced, never edited, so undo stays exact).</summary>
    public static List<Guid> Toggle(IReadOnlyList<Guid> current, Guid id, bool exclude)
    {
        List<Guid> next = [.. current.Where(g => g != id)];
        if (exclude)
        {
            next.Add(id);
        }

        return next;
    }
}

/// <summary>SELECTION-tab properties of the single selected device (D-089). Every setter is undoable.</summary>
public sealed class DeviceViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>Wraps a device.</summary>
    public DeviceViewModel(Device model, MainViewModel owner)
    {
        Model = model;
        _owner = owner;
        DrawQueueCommand = new RelayCommand(() => _owner.StartQueuePath(Model.Id));
        ClearQueueCommand = new RelayCommand(() => _owner.Editor.ClearQueuePath(Model.Id));
    }

    /// <summary>Starts DRAW QUEUE in the plan editor (D-153, D-163).</summary>
    public RelayCommand DrawQueueCommand { get; }

    /// <summary>Removes the drawn queue path, back to the automatic lines.</summary>
    public RelayCommand ClearQueueCommand { get; }

    /// <summary>The wrapped device.</summary>
    public Device Model { get; }

    /// <summary>Display name (blank names are rejected).</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                Reject("A device needs a name.");
                return;
            }

            _owner.Apply("Rename device", () => Model.Name, v => Model.Name = v, trimmed);
        }
    }

    /// <summary>Preset and type tag.</summary>
    public string PresetText
    {
        get
        {
            TemplateBase? template = Model.TemplateId is { } id ? _owner.Study.Library.Find(id) : null;
            template ??= BuiltInLibrary.Instance.FindByKey(Model.PresetKey);
            return $"{template?.Name ?? Model.PresetKey} · {Model.TypeTag}";
        }
    }

    /// <summary>Width across the service direction (0.1–30 m).</summary>
    public double WidthM
    {
        get => Model.WidthM;
        set => SetSize(value, Model.WidthM, "width", v => Model.WidthM = v);
    }

    /// <summary>Depth along the service direction (0.1–30 m).</summary>
    public double DepthM
    {
        get => Model.DepthM;
        set => SetSize(value, Model.DepthM, "depth", v => Model.DepthM = v);
    }

    /// <summary>Lanes or servers (1–20).</summary>
    public int Lanes
    {
        get => Model.Lanes;
        set
        {
            if (value is < 1 or > 20)
            {
                Reject("Lanes must be between 1 and 20.");
                return;
            }

            _owner.Apply("Change device lanes", () => Model.Lanes, v => Model.Lanes = v, value);
        }
    }

    /// <summary>Visual height (0.1–5 m).</summary>
    public double HeightM
    {
        get => Model.HeightM;
        set => SetSize(value, Model.HeightM, "height", v => Model.HeightM = v, max: 5);
    }

    /// <summary>Service direction in degrees (rotates about the footprint centre).</summary>
    public double AngleDeg
    {
        get => Math.Round(Model.AngleDeg, 2);
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                Reject("Type an angle in degrees.");
                return;
            }

            double angle = ConnectorFactory.NormaliseAngle(value);
            if (Model.QueuePath is { Count: > 0 } && Math.Abs(angle - Model.AngleDeg) > 1e-9)
            {
                // A drawn queue turns with the device about its centre.
                _owner.Execute(StateActions.Devices("Rotate device", [(Model, DeviceFactory.Rotated(Model, Model.Position, angle - Model.AngleDeg))], _owner.NotifyModelChanged));
                return;
            }

            _owner.Apply("Rotate device", () => Model.AngleDeg, v => Model.AngleDeg = v, angle);
        }
    }

    /// <summary>Longest queue per lane or server (1–500, D-153); a drawn snake holds this many per lane.</summary>
    public int MaxQueue
    {
        get => Model.MaxQueue;
        set
        {
            if (value is < 1 or > QueueLines.MaxSlots)
            {
                Reject(string.Create(CultureInfo.InvariantCulture, $"Max queue must be between 1 and {QueueLines.MaxSlots} people."));
                return;
            }

            _owner.Apply("Change device max queue", () => Model.MaxQueue, v => Model.MaxQueue = v, value);
        }
    }

    /// <summary>True when the device has a drawn queue path (shows CLEAR QUEUE).</summary>
    public bool HasDrawnQueue => Model.QueuePath is { Count: >= 2 };

    /// <summary>How its queue is laid out.</summary>
    public string QueueText
    {
        get
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            if (HasDrawnQueue)
            {
                double length = 0;
                for (int k = 1; k < Model.QueuePath.Count; k++)
                {
                    length += Point2.Distance(Model.QueuePath[k - 1], Model.QueuePath[k]);
                }

                string lanes = Model.Lanes > 1 ? string.Create(c, $", feeding all {Model.Lanes} lanes") : string.Empty;
                return string.Create(c, $"Drawn queue: one snake, {length:0.0} m{lanes} (D-163).{(Model.PassThrough ? " The far side keeps its automatic lines." : string.Empty)}");
            }

            string sides = Model.PassThrough ? " on both sides" : string.Empty;
            return string.Create(c, $"Automatic lines: one per lane{sides}, straight out and bending along walls, up to {Model.MaxQueue} people each. DRAW QUEUE for a shared snake.");
        }
    }

    /// <summary>How people use it.</summary>
    public string FlowText => Model.PassThrough
        ? "Pass-through: people walk through the lanes (only the dividers block the floor)."
        : "Served from the front: the whole footprint blocks the floor.";

    /// <summary>Re-reads every property from the model.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private void SetSize(double value, double current, string what, Action<double> setter, double max = 30)
    {
        if (double.IsNaN(value) || value < 0.1 || value > max)
        {
            Reject(string.Create(CultureInfo.InvariantCulture, $"Device {what} must be between 0.1 and {max:0} m."));
            return;
        }

        double rounded = Math.Round(value, 3);
        if (Math.Abs(rounded - current) > 1e-9)
        {
            double old = current;
            _owner.Apply($"Change device {what}", () => old, setter, rounded);
        }
    }

    private void Reject(string message)
    {
        _owner.Ticker = message;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// Properties of one underlay (D-083–D-085), used for the SELECTION panel and the LEVEL tab's underlay list.
/// Every setter is undoable.
/// </summary>
public sealed class UnderlayViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>Wraps an underlay.</summary>
    public UnderlayViewModel(Underlay model, MainViewModel owner)
    {
        Model = model;
        _owner = owner;
        SetScaleCommand = new RelayCommand(() => _owner.Editor.StartCalibration(Model.Id));
        SelectCommand = new RelayCommand(() => _owner.SelectObject(Model.Id));
    }

    /// <summary>The wrapped underlay.</summary>
    public Underlay Model { get; }

    /// <summary>Starts SET SCALE (reference distance, D-084).</summary>
    public RelayCommand SetScaleCommand { get; }

    /// <summary>Selects the underlay in the viewport (LEVEL tab list).</summary>
    public RelayCommand SelectCommand { get; }

    /// <summary>Display name (blank names are rejected).</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "An underlay needs a name.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Rename underlay", () => Model.Name, v => Model.Name = v, trimmed);
        }
    }

    /// <summary>Opacity 0–1.</summary>
    public double Opacity
    {
        get => Math.Round(Model.Opacity, 2);
        set
        {
            if (double.IsNaN(value) || value < 0 || value > 1)
            {
                _owner.Ticker = "Opacity must be between 0 and 1.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Change underlay opacity", () => Model.Opacity, v => Model.Opacity = v, Math.Round(value, 2));
        }
    }

    /// <summary>Drawn in plan.</summary>
    public bool IsVisible
    {
        get => Model.IsVisible;
        set => _owner.Apply(value ? "Show underlay" : "Hide underlay", () => Model.IsVisible, v => Model.IsVisible = v, value);
    }

    /// <summary>Locked underlays can't be picked or moved.</summary>
    public bool IsLocked
    {
        get => Model.IsLocked;
        set => _owner.Apply(value ? "Lock underlay" : "Unlock underlay", () => Model.IsLocked, v => Model.IsLocked = v, value);
    }

    /// <summary>Rotation in degrees (about the image centre).</summary>
    public double AngleDeg
    {
        get => Math.Round(Model.AngleDeg, 2);
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                _owner.Ticker = "Type an angle in degrees.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Rotate underlay", () => Model.AngleDeg, v => Model.AngleDeg = v, ConnectorFactory.NormaliseAngle(value));
        }
    }

    /// <summary>Plan width of the image in metres; changing it rescales about the centre.</summary>
    public double WidthM
    {
        get => Math.Round(UnderlayGeometry.Size(Model).Width, 3);
        set
        {
            if (double.IsNaN(value) || value <= 0 || Model.PixelWidth <= 0)
            {
                _owner.Ticker = "Width must be greater than zero.";
                OnPropertyChanged();
                return;
            }

            _owner.Apply("Scale underlay", () => Model.MetresPerPixel, v => Model.MetresPerPixel = v, value / Model.PixelWidth);
        }
    }

    /// <summary>Scale and size readout.</summary>
    public string ScaleText
    {
        get
        {
            (double w, double h) = UnderlayGeometry.Size(Model);
            string image = _owner.Study.UnderlayImages.ContainsKey(Model.Id) ? string.Empty : " · IMAGE MISSING";
            return string.Create(CultureInfo.InvariantCulture,
                $"{Model.PixelWidth} × {Model.PixelHeight} px · {Model.MetresPerPixel * 1000:0.##} mm/px · {w:0.0} × {h:0.0} m{image}");
        }
    }

    /// <summary>Re-reads every property from the model.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
