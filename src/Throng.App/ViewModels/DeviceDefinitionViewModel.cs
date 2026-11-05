using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Library;

namespace Throng.App.ViewModels;

/// <summary>
/// SELECTION ▸ DEVICE DEFINITION (round 017, D-286–D-290): a device definition picked in LIBRARY ▸ DEVICES or from
/// a placed device. Built-ins are edited in place for this study (a modified copy under the built-in's id, RESET
/// puts the built-in back); the study's own definitions can also be deleted when nothing uses them. Placed devices
/// follow their definition (D-288). The definition is looked up by id on every read, because the first edit of a
/// built-in swaps in the study's copy.
/// </summary>
public sealed class DeviceDefinitionViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>Wraps the definition with this id.</summary>
    public DeviceDefinitionViewModel(Guid id, MainViewModel owner)
    {
        Id = id;
        _owner = owner;
        DuplicateCommand = new RelayCommand(() => _owner.DuplicateDefinition(Template));
        ResetCommand = new RelayCommand(() => _owner.ResetDefinition(Template));
        DeleteCommand = new RelayCommand(() => _owner.DeleteDefinition(Template));
        PlaceCommand = new RelayCommand(() => _owner.PlaceDefinition(Template));
        ExportCommand = new RelayCommand(() => _owner.ExportDevice(Template));
        SaveToMineCommand = new RelayCommand(() => _owner.SaveToMyLibrary(Template));
        UpdateFromMineCommand = new RelayCommand(() => _owner.UpdateFromMyLibrary(Id));
        ServiceTime = new DistributionEditorViewModel("Service time", "s", () => Template.ServiceTime, d => Edit("Change service time", t => t.ServiceTime = d));
    }

    /// <summary>Id of the definition.</summary>
    public Guid Id { get; }

    /// <summary>The definition as the study sees it now (its copy, else the built-in).</summary>
    public DeviceTemplate Template => _owner.CurrentDevice(Id) ?? Fallback;

    /// <summary>Makes an editable copy with a new name and key.</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Puts a modified built-in back to the built-in's values.</summary>
    public RelayCommand ResetCommand { get; }

    /// <summary>Deletes one of the study's own definitions that no placed device uses.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Starts PLACE with this definition.</summary>
    public RelayCommand PlaceCommand { get; }

    /// <summary>Writes the definition to a .throngdevice file.</summary>
    public RelayCommand ExportCommand { get; }

    /// <summary>SAVE TO MY LIBRARY (round 020, D-342): copies this definition into your library.</summary>
    public RelayCommand SaveToMineCommand { get; }

    /// <summary>UPDATE FROM MY LIBRARY (D-344): your library's version back onto the study's copy (one undo step).</summary>
    public RelayCommand UpdateFromMineCommand { get; }

    /// <summary>True when the study's copy differs from your library's (D-344, D-351).</summary>
    public bool DiffersFromMine => _owner.MineDiffers(Id);

    /// <summary>The line about your library (MINE, DIFFERS…); empty when it isn't in your library.</summary>
    public string MineText => _owner.MineStatusText(Id);

    /// <summary>True when <see cref="MineText"/> shows.</summary>
    public bool HasMineText => MineText.Length > 0;

    /// <summary>The service time distribution.</summary>
    public DistributionEditorViewModel ServiceTime { get; }

    /// <summary>True for built-ins and their study copies.</summary>
    public bool IsBuiltIn => StudyDefinitions.IsBuiltIn(Template);

    /// <summary>True when the study's copy of a built-in differs from it.</summary>
    public bool IsModified => StudyDefinitions.IsModified(_owner.Study, Template);

    /// <summary>True for the study's own (duplicated, new or imported) definitions; not yours until the study uses them.</summary>
    public bool IsOwn => !IsBuiltIn && _owner.Study.Library.Find(Id) is not null;

    /// <summary>Point devices have service settings; zone presets are areas.</summary>
    public bool IsPoint => Template.Kind == DeviceKind.Point;

    /// <summary>Zone presets (areas): only name, description and colour apply.</summary>
    public bool IsArea => !IsPoint;

    /// <summary>Where it came from and what editing it does.</summary>
    public string SourceText => _owner.IsMineOnly(Id)
        ? "From your library. Edits change it for this study only (SAVE TO MY LIBRARY puts them back in your library)."
        : IsBuiltIn
        ? IsModified
            ? "Built-in, modified in this study. RESET TO BUILT-IN puts the shipped values back. Edits can be undone."
            : "Built-in. Edits change it for this study only (DUPLICATE makes a separate type instead)."
        : Template.Source == TemplateSource.Imported
            ? "Imported into this study. Edits can be undone."
            : "Your device, saved in this study. Edits can be undone.";

    /// <summary>How many placed devices follow it.</summary>
    public string UsageText
    {
        get
        {
            int count = _owner.DeviceDefinitionUseCount(Template);
            return count switch
            {
                0 => "No placed device uses it yet: PLACE, or drag it from LIBRARY onto the plan.",
                1 => "1 placed device follows it (service, type, pass-through, colour); size, lanes and angle stay per device.",
                _ => string.Create(CultureInfo.InvariantCulture, $"{count} placed devices follow it (service, type, pass-through, colour); size, lanes and angle stay per device."),
            };
        }
    }

    /// <summary>Display name (blank rejected).</summary>
    public string Name
    {
        get => Template.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A device definition needs a name.";
                OnPropertyChanged(nameof(Name));
                return;
            }

            Edit("Rename device definition", t => t.Name = trimmed);
        }
    }

    /// <summary>Description (the LIBRARY tooltip).</summary>
    public string Description
    {
        get => Template.Description;
        set => Edit("Change device description", t => t.Description = (value ?? string.Empty).Trim());
    }

    /// <summary>Colour as #RRGGBB (placed devices follow).</summary>
    public string ColorHex
    {
        get => Template.ColorHex;
        set
        {
            string text = (value ?? string.Empty).Trim();
            if (!IsHexColour(text))
            {
                _owner.Ticker = "Colour must be #RRGGBB, e.g. #FACC15.";
                OnPropertyChanged(nameof(ColorHex));
                return;
            }

            Edit("Change device colour", t => t.ColorHex = text.ToUpperInvariant());
        }
    }

    /// <summary>Type tag journeys route to (placed devices follow; blank rejected).</summary>
    public string TypeTag
    {
        get => Template.TypeTag;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A device needs a type tag: journeys' device steps go to a type.";
                OnPropertyChanged(nameof(TypeTag));
                return;
            }

            Edit("Change device type", t => t.TypeTag = trimmed);
        }
    }

    /// <summary>Type tags the study knows, for the editable combo.</summary>
    public IReadOnlyList<string> TypeTagOptions => StudyDefinitions.KnownDeviceTypes(_owner.Study);

    /// <summary>Lanes / servers a new placement gets (1–64).</summary>
    public int Servers
    {
        get => Template.Servers;
        set => Edit("Change default lanes", t => t.Servers = Math.Clamp(value, 1, 64));
    }

    /// <summary>Width a new placement gets (0.1–30 m).</summary>
    public double WidthM
    {
        get => Template.WidthM;
        set => Edit("Change default width", t => t.WidthM = Clamp(value, 0.1, 30, t.WidthM));
    }

    /// <summary>Depth a new placement gets (0.1–30 m).</summary>
    public double DepthM
    {
        get => Template.DepthM;
        set => Edit("Change default depth", t => t.DepthM = Clamp(value, 0.1, 30, t.DepthM));
    }

    /// <summary>People walk through it (gates) rather than stop at its service side (placed devices follow).</summary>
    public bool PassThrough
    {
        get => Template.PassThrough;
        set => Edit(value ? "Make device pass-through" : "Make device stop-and-serve", t => t.PassThrough = value);
    }

    /// <summary>Choice weight in seconds: positive draws people, negative puts them off (−600 to 600).</summary>
    public double ChoiceWeight
    {
        get => Template.ChoiceWeight;
        set => Edit("Change choice weight", t => t.ChoiceWeight = Clamp(value, -600, 600, t.ChoiceWeight));
    }

    /// <summary>Accessible by default (placed devices can still override it).</summary>
    public bool Accessible
    {
        get => Template.Accessible;
        set => Edit(value ? "Make device type accessible" : "Make device type not accessible", t => t.Accessible = value);
    }

    /// <summary>Re-reads everything (after an edit or undo).</summary>
    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        ServiceTime.Refresh();
    }

    private static readonly DeviceTemplate Fallback = new() { Name = "(gone)", TypeTag = string.Empty };

    private void Edit(string description, Action<DeviceTemplate> change) => _owner.EditDeviceDefinition(Id, description, change);

    private static double Clamp(double value, double min, double max, double current) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : current;

    internal static bool IsHexColour(string text) =>
        text.Length == 7 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);
}
