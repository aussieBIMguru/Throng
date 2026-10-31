using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Services;
using Throng.Core.Common;
using Throng.Core.Editing;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.IO;

namespace Throng.App.ViewModels;

/// <summary>
/// Device and agent profile definitions (round 017, D-286–D-290): clicking a row in LIBRARY ▸ DEVICES or ▸ AGENTS
/// opens its definition in SELECTION. Built-ins are edited in place for the study (the study's copy under the
/// built-in's id, D-287), DUPLICATE and NEW make the study's own, RESET TO BUILT-IN and DELETE tidy up, and placed
/// devices follow their definition (D-288). Device definitions export and import as <c>.throngdevice</c> files.
/// Every change is one undo step; all of it is blocked while a run is active.
/// </summary>
public sealed partial class MainViewModel
{
    private DeviceDefinitionViewModel? _selectedDeviceDefinition;
    private ProfileDefinitionViewModel? _selectedProfileDefinition;
    private Guid? _libraryDeviceId;
    private RelayCommand? _newDeviceDefinitionCommand;
    private RelayCommand? _newProfileDefinitionCommand;
    private RelayCommand? _exportDeviceCommand;
    private RelayCommand? _importDeviceCommand;

    /// <summary>The device definition shown in SELECTION, or null.</summary>
    public DeviceDefinitionViewModel? SelectedDeviceDefinition => _selectedDeviceDefinition;

    /// <summary>True when a device definition is shown in SELECTION.</summary>
    public bool HasDeviceDefinitionSelection => _selectedDeviceDefinition is not null;

    /// <summary>The agent profile shown in SELECTION, or null.</summary>
    public ProfileDefinitionViewModel? SelectedProfileDefinition => _selectedProfileDefinition;

    /// <summary>True when an agent profile is shown in SELECTION.</summary>
    public bool HasProfileDefinitionSelection => _selectedProfileDefinition is not null;

    /// <summary>True when SELECTION shows something from LIBRARY (a journey or a definition) rather than the plan's selection.</summary>
    public bool HasLibrarySelection => _selectedJourney is not null || _selectedDeviceDefinition is not null || _selectedProfileDefinition is not null;

    /// <summary>LIBRARY ▸ DEVICES ▸ NEW.</summary>
    public RelayCommand NewDeviceDefinitionCommand => _newDeviceDefinitionCommand ??= new RelayCommand(NewDeviceDefinition);

    /// <summary>LIBRARY ▸ AGENTS ▸ NEW.</summary>
    public RelayCommand NewProfileDefinitionCommand => _newProfileDefinitionCommand ??= new RelayCommand(NewProfileDefinition);

    /// <summary>LIBRARY ▸ DEVICES ▸ EXPORT…: the clicked device definition.</summary>
    public RelayCommand ExportDeviceCommand => _exportDeviceCommand ??= new RelayCommand(() =>
    {
        if (_libraryDeviceId is { } id && CurrentDevice(id) is { } device)
        {
            ExportDevice(device);
        }
        else
        {
            Ticker = "Click a device in LIBRARY ▸ DEVICES first, then EXPORT.";
        }
    });

    /// <summary>LIBRARY ▸ DEVICES ▸ IMPORT….</summary>
    public RelayCommand ImportDeviceCommand => _importDeviceCommand ??= new RelayCommand(ImportDevice);

    /// <summary>The device definition with this id as the study sees it (its copy first, then yours, then the built-in), or null.</summary>
    public DeviceTemplate? CurrentDevice(Guid id) => StudyDefinitions.FindDevice(_study, id, Mine);

    /// <summary>Number of placed devices that follow a definition.</summary>
    public int DeviceDefinitionUseCount(DeviceTemplate template) =>
        StudyDefinitions.DevicesUsing(_study, StudyDefinitions.FindDevice(_study, template.Id) ?? template).Count;

    /// <summary>Opens a device or profile definition in SELECTION (one thing there at a time).</summary>
    public void SelectDefinition(TemplateBase template)
    {
        Editor.ClearSelection();
        ClearJourneySelection();
        switch (template)
        {
            case DeviceTemplate device:
                _libraryDeviceId = device.Id;
                if (_selectedDeviceDefinition?.Id != device.Id)
                {
                    _selectedDeviceDefinition = new DeviceDefinitionViewModel(device.Id, this);
                }

                SetProfileDefinition(null);
                break;

            case AgentProfileTemplate profile:
                _libraryProfile = profile;
                if (_selectedProfileDefinition?.Id != profile.Id)
                {
                    _selectedProfileDefinition = new ProfileDefinitionViewModel(profile.Id, this);
                }

                _selectedDeviceDefinition = null;
                break;

            default:
                return;
        }

        RaiseDefinitionSelection();
        Library.MarkSelected(template.Id);
        PropertiesTabIndex = PropertiesTabSelection;
    }

    /// <summary>Applies a change to a device definition as one undo step; placed devices follow (D-288).</summary>
    /// <param name="id">Definition id (the built-in's for a built-in: its study copy is made on the first edit).</param>
    /// <param name="description">Undo / ticker text.</param>
    /// <param name="change">Edits a copy of the definition.</param>
    public void EditDeviceDefinition(Guid id, string description, Action<DeviceTemplate> change)
    {
        if (!CheckDefinitionEditable() || CurrentDevice(id) is not { } current)
        {
            return;
        }

        DeviceTemplate target = StudyDefinitions.EnsureStudyCopy(_study, current);
        var next = (DeviceTemplate)TemplateCloner.Clone(target);
        change(next);
        if (StudyDefinitions.SameValues(target, next))
        {
            RefreshDefinitionPanels();
            return;
        }

        var actions = new List<IUndoableAction> { DefinitionState(description, target, next) };
        List<(Device Target, Device NewState)> placed = [];
        foreach (Device device in StudyDefinitions.DevicesUsing(_study, target))
        {
            if (!string.Equals(device.TypeTag, next.TypeTag, StringComparison.Ordinal)
                || device.PassThrough != next.PassThrough
                || !string.Equals(device.ColorHex, next.ColorHex, StringComparison.OrdinalIgnoreCase))
            {
                Device state = device.Clone();
                state.TypeTag = next.TypeTag;
                state.PassThrough = next.PassThrough;
                state.ColorHex = next.ColorHex;
                placed.Add((device, state));
            }
        }

        if (placed.Count > 0)
        {
            actions.Add(StateActions.Devices(description, placed, null));
        }

        Execute(new CompositeAction(description, actions, OnModelChanged));
        Ticker = placed.Count switch
        {
            0 => description,
            1 => $"{description} ▸ 1 placed device follows",
            int n => string.Create(CultureInfo.InvariantCulture, $"{description} ▸ {n} placed devices follow"),
        };
    }

    /// <summary>Applies a change to an agent profile as one undo step (runs read profiles directly).</summary>
    public void EditProfileDefinition(Guid id, string description, Action<AgentProfileTemplate> change)
    {
        if (!CheckDefinitionEditable() || StudyTemplates.FindProfile(_study, id, Mine) is not { } current)
        {
            return;
        }

        if (current.DefaultJourneyId is { } journeyId)
        {
            // A profile from your library brings its default journey (D-346, D-350).
            EmbedForUse(journeyId);
        }

        AgentProfileTemplate target = StudyDefinitions.EnsureStudyCopy(_study, current);
        var next = (AgentProfileTemplate)TemplateCloner.Clone(target);
        change(next);
        if (StudyDefinitions.SameValues(target, next))
        {
            RefreshDefinitionPanels();
            return;
        }

        Execute(new CompositeAction(description, [DefinitionState(description, target, next)], OnModelChanged));
        Ticker = description;
    }

    /// <summary>DUPLICATE: the study's own copy with a new name and key, selected.</summary>
    public void DuplicateDefinition(TemplateBase source)
    {
        if (!CheckDefinitionEditable())
        {
            return;
        }

        switch (source)
        {
            case DeviceTemplate device:
            {
                DeviceTemplate copy = StudyDefinitions.CreateDeviceCopy(_study, device);
                Execute(new AddItemsAction<DeviceTemplate>($"Duplicate device {device.Name}", _study.Library.Devices, [copy], OnModelChanged));
                SelectDefinition(copy);
                Ticker = $"{copy.Name} ▸ same type tag ({copy.TypeTag}), so journeys use it too; change it to make a new type.";
                break;
            }

            case AgentProfileTemplate profile:
            {
                AgentProfileTemplate copy = StudyDefinitions.CreateProfileCopy(_study, profile);
                Execute(new AddItemsAction<AgentProfileTemplate>($"Duplicate profile {profile.Name}", _study.Library.AgentProfiles, [copy], OnModelChanged));
                SelectDefinition(copy);
                Ticker = $"{copy.Name} ▸ edit it here, then pick it on a spawn zone's mix line.";
                break;
            }
        }
    }

    /// <summary>RESET TO BUILT-IN: the built-in's values back on the study's copy (placed devices follow).</summary>
    public void ResetDefinition(TemplateBase template)
    {
        if (StudyDefinitions.BuiltInOf(template) is not { } builtIn)
        {
            Ticker = "Only built-ins can be reset; this one is the study's own.";
            return;
        }

        switch (builtIn)
        {
            case DeviceTemplate device:
                EditDeviceDefinition(device.Id, $"Reset {device.Name} to built-in", t => StudyDefinitions.CopyValues(t, device));
                break;
            case AgentProfileTemplate profile:
                EditProfileDefinition(profile.Id, $"Reset {profile.Name} to built-in", t => StudyDefinitions.CopyValues(t, profile));
                break;
        }
    }

    /// <summary>DELETE: removes one of the study's own definitions when nothing uses it.</summary>
    public void DeleteDefinition(TemplateBase template)
    {
        if (!CheckDefinitionEditable())
        {
            return;
        }

        if (StudyDefinitions.IsBuiltIn(template))
        {
            Ticker = "Built-ins can't be deleted (RESET TO BUILT-IN undoes this study's changes).";
            return;
        }

        if (_study.Library.Find(template.Id) is null)
        {
            Ticker = $"{template.Name} is in your library, not this study: right-click it in LIBRARY to delete it from your library.";
            return;
        }

        switch (template)
        {
            case DeviceTemplate device:
            {
                int uses = DeviceDefinitionUseCount(device);
                if (uses > 0)
                {
                    Ticker = $"{device.Name} is used by {uses} placed device{(uses == 1 ? string.Empty : "s")}: delete them or MAKE UNIQUE first.";
                    return;
                }

                ClearDefinitionSelection();
                Execute(new RemoveItemsAction<DeviceTemplate>($"Delete device {device.Name}", _study.Library.Devices, [device], OnModelChanged));
                break;
            }

            case AgentProfileTemplate profile:
            {
                int uses = StudyDefinitions.ProfileUseCount(_study, profile.Id);
                if (uses > 0)
                {
                    Ticker = $"{profile.Name} is used by {uses} mix line{(uses == 1 ? string.Empty : "s")}: pick another profile there first.";
                    return;
                }

                ClearDefinitionSelection();
                Execute(new RemoveItemsAction<AgentProfileTemplate>($"Delete profile {profile.Name}", _study.Library.AgentProfiles, [profile], OnModelChanged));
                break;
            }
        }
    }

    /// <summary>PLACE: starts the PLACE tool with this device definition.</summary>
    public void PlaceDefinition(DeviceTemplate device)
    {
        if (device.Kind != DeviceKind.Point)
        {
            Ticker = $"{device.Name} is an area: draw it as a zone.";
            return;
        }

        UseTemplate(new TemplateItemViewModel(CurrentDevice(device.Id) ?? device));
    }

    /// <summary>
    /// MAKE UNIQUE on a placed device: a copy of its definition (new name and key) that only this device follows,
    /// in one undo step, then opens it in SELECTION.
    /// </summary>
    public void MakeDeviceUnique(Device device)
    {
        if (!CheckDefinitionEditable())
        {
            return;
        }

        DeviceTemplate source = StudyTemplates.FindDeviceTemplate(_study, device) ?? StudyDefinitions.CreateNewDevice(_study);
        DeviceTemplate copy = StudyDefinitions.CreateDeviceCopy(_study, source);
        copy.Name = UniqueDeviceName($"{device.Name} type");
        Device state = device.Clone();
        state.TemplateId = copy.Id;
        state.PresetKey = copy.Key;
        state.TypeTag = copy.TypeTag;
        state.PassThrough = copy.PassThrough;
        state.ColorHex = copy.ColorHex;
        IUndoableAction[] actions =
        [
            new AddItemsAction<DeviceTemplate>("Add definition", _study.Library.Devices, [copy], null),
            StateActions.Devices("Follow definition", [(device, state)], null),
        ];
        Execute(new CompositeAction($"Make {device.Name} unique", actions, OnModelChanged));
        SelectDefinition(copy);
        Ticker = $"{device.Name} now has its own definition '{copy.Name}' ▸ edit it here; other devices keep theirs.";
    }

    /// <summary>EXPORT…: writes a device definition to a .throngdevice file.</summary>
    public void ExportDevice(DeviceTemplate device)
    {
        DeviceTemplate current = CurrentDevice(device.Id) ?? device;
        string? path = _dialogs.PickDeviceSavePath(SafeName(current.Name) + DeviceFile.Extension);
        if (path is null)
        {
            return;
        }

        Result saved = DeviceFile.Save(DeviceReconciler.BuildBundle(current), path);
        if (!saved.IsSuccess)
        {
            AppLog.Warn(saved.Error ?? "Device export failed.");
            _dialogs.ShowError(saved.Error ?? "The device couldn't be exported.");
            return;
        }

        Ticker = $"EXPORTED ▸ {current.Name} ▸ {Path.GetFileName(path)}";
    }

    /// <summary>EXPORT… from the profile panel: the 016 profile exchange (D-275) for this profile.</summary>
    public void ExportProfileDefinition(AgentProfileTemplate profile)
    {
        _libraryProfile = StudyTemplates.FindProfile(_study, profile.Id, Mine) ?? profile;
        ExportProfileCommand.Execute(null);
    }

    private void ImportDevice()
    {
        if (!CheckDefinitionEditable())
        {
            return;
        }

        string? path = _dialogs.PickDeviceOpenPath();
        if (path is null)
        {
            return;
        }

        Result<DeviceBundle> loaded = DeviceFile.Load(path);
        if (!loaded.IsSuccess || loaded.Value is not { } bundle)
        {
            _dialogs.ShowError(loaded.Error ?? "The device couldn't be read.");
            return;
        }

        List<ReconcileItem> items = DeviceReconciler.Find(_study, bundle);
        IReadOnlyList<ReconcileChoice> choices = [];
        if (items.Count > 0)
        {
            if (_dialogs.ReconcileDevice(bundle.Device.Name, items) is not { } answered)
            {
                return;
            }

            choices = answered;
        }

        DeviceTemplate device = DeviceReconciler.Apply(_study, bundle, items, choices);
        Execute(new AddItemsAction<DeviceTemplate>($"Import device {device.Name}", _study.Library.Devices, [device], OnModelChanged));
        SelectDefinition(device);
        Ticker = $"IMPORTED ▸ {device.Name} ({device.TypeTag}) ▸ PLACE it, or drag it from LIBRARY ▸ DEVICES.";
    }

    private void NewDeviceDefinition()
    {
        if (!CheckDefinitionEditable())
        {
            return;
        }

        DeviceTemplate device = StudyDefinitions.CreateNewDevice(_study);
        Execute(new AddItemsAction<DeviceTemplate>("New device definition", _study.Library.Devices, [device], OnModelChanged));
        SelectDefinition(device);
        Ticker = $"{device.Name} ▸ set its type tag (journeys' device steps go to a type), service time and size, then PLACE it.";
    }

    private void NewProfileDefinition()
    {
        if (!CheckDefinitionEditable())
        {
            return;
        }

        AgentProfileTemplate profile = StudyDefinitions.CreateNewProfile(_study);
        Execute(new AddItemsAction<AgentProfileTemplate>("New profile", _study.Library.AgentProfiles, [profile], OnModelChanged));
        SelectDefinition(profile);
        Ticker = $"{profile.Name} ▸ edit it here, then pick it on a spawn zone's mix line.";
    }

    /// <summary>Drops the definition panels (selecting in the plan or a journey replaces them).</summary>
    private void ClearDefinitionSelection()
    {
        if (_selectedDeviceDefinition is null && _selectedProfileDefinition is null)
        {
            return;
        }

        _selectedDeviceDefinition = null;
        _selectedProfileDefinition = null;
        Library.MarkSelected(_selectedJourney?.Journey.Id);
        RaiseDefinitionSelection();
    }

    /// <summary>
    /// After a model change: LIBRARY ▸ DEVICES and the PLACE presets from the study, and the open definition panel
    /// re-read (or dropped when its definition left the study, e.g. an undone NEW).
    /// </summary>
    private void RefreshDefinitions()
    {
        // The study's own, then yours (MINE), then built-ins (D-343).
        IReadOnlyList<DeviceTemplate> devices = StudyDefinitions.AvailableDevices(_study, Mine);
        Library.SetDevices(devices);
        Library.RefreshNotes(LibraryNote);
        RefreshPlacePresets(devices);

        if (_selectedDeviceDefinition is { } device && CurrentDevice(device.Id) is null)
        {
            ClearDefinitionSelection();
        }

        if (_selectedProfileDefinition is { } profile && StudyTemplates.FindProfile(_study, profile.Id, Mine) is null)
        {
            ClearDefinitionSelection();
        }

        RefreshDefinitionPanels();
    }

    private void RefreshDefinitionPanels()
    {
        _selectedDeviceDefinition?.Refresh();
        _selectedProfileDefinition?.Refresh();
    }

    private void SetProfileDefinition(ProfileDefinitionViewModel? profile) => _selectedProfileDefinition = profile;

    private void RaiseDefinitionSelection()
    {
        OnPropertyChanged(nameof(SelectedDeviceDefinition));
        OnPropertyChanged(nameof(HasDeviceDefinitionSelection));
        OnPropertyChanged(nameof(SelectedProfileDefinition));
        OnPropertyChanged(nameof(HasProfileDefinitionSelection));
        OnPropertyChanged(nameof(HasLibrarySelection));
        OnPropertyChanged(nameof(SelectionText));
    }

    private bool CheckDefinitionEditable()
    {
        // Editing is locked while a run is on (D-097); every path checks it itself (round 014 lesson).
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return false;
        }

        return true;
    }

    private string UniqueDeviceName(string baseName)
    {
        var names = StudyDefinitions.AvailableDevices(_study).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string name = baseName;
        for (int n = 2; names.Contains(name); n++)
        {
            name = string.Create(CultureInfo.InvariantCulture, $"{baseName} {n}");
        }

        return name;
    }

    private static ReplaceStateAction<T> DefinitionState<T>(string description, T target, T next)
        where T : TemplateBase =>
        new(description, [(target, next)], t => (T)TemplateCloner.Clone(t), (into, from) => StudyDefinitions.CopyValues(into, from), null);
}
