using System.IO;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Services;
using Throng.Core.Common;
using Throng.Core.Editing;
using Throng.Core.Library;
using Throng.IO;

namespace Throng.App.ViewModels;

/// <summary>
/// Profile exchange (BRIEF §8.6, round 016, D-275): LIBRARY ▸ AGENTS ▸ EXPORT writes the clicked profile and the
/// journeys its mix lines use to a <c>.throngprofile</c>; IMPORT reads one, asks keep / map / drop for anything the
/// study lacks (<see cref="ProfileReconciler"/>), and adds the profile and journeys to the study library in one
/// undoable step.
/// </summary>
public sealed partial class MainViewModel
{
    private AgentProfileTemplate? _libraryProfile;
    private RelayCommand? _exportProfileCommand;
    private RelayCommand? _importProfileCommand;

    /// <summary>LIBRARY ▸ AGENTS ▸ EXPORT…: the clicked profile.</summary>
    public RelayCommand ExportProfileCommand => _exportProfileCommand ??= new RelayCommand(ExportProfile);

    /// <summary>LIBRARY ▸ AGENTS ▸ IMPORT….</summary>
    public RelayCommand ImportProfileCommand => _importProfileCommand ??= new RelayCommand(ImportProfile);

    /// <summary>A click on an AGENTS row picks the profile EXPORT writes.</summary>
    private void SelectLibraryProfile(AgentProfileTemplate profile)
    {
        _libraryProfile = profile;
        Library.MarkSelected(profile.Id);
        Ticker = $"{profile.Name} ▸ edit it in SELECTION; EXPORT writes it (and the journeys it's used with) to a .throngprofile.";
    }

    private void ExportProfile()
    {
        if (_libraryProfile is not { } profile)
        {
            Ticker = "Click a profile in LIBRARY ▸ AGENTS first, then EXPORT.";
            return;
        }

        string? path = _dialogs.PickProfileSavePath(SafeName(profile.Name) + ProfileFile.Extension);
        if (path is null)
        {
            return;
        }

        ProfileBundle bundle = ProfileReconciler.BuildBundle(_study, profile);
        Result saved = ProfileFile.Save(bundle, path);
        if (!saved.IsSuccess)
        {
            AppLog.Warn(saved.Error ?? "Profile export failed.");
            _dialogs.ShowError(saved.Error ?? "The profile couldn't be exported.");
            return;
        }

        Ticker = $"EXPORTED ▸ {profile.Name} with {bundle.Journeys.Count} journey{(bundle.Journeys.Count == 1 ? string.Empty : "s")} ▸ {Path.GetFileName(path)}";
    }

    private void ImportProfile()
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        string? path = _dialogs.PickProfileOpenPath();
        if (path is null)
        {
            return;
        }

        Result<ProfileBundle> loaded = ProfileFile.Load(path);
        if (!loaded.IsSuccess || loaded.Value is not { } bundle)
        {
            _dialogs.ShowError(loaded.Error ?? "The profile couldn't be read.");
            return;
        }

        List<ReconcileItem> items = ProfileReconciler.Find(_study, bundle);
        IReadOnlyList<ReconcileChoice> choices = [];
        if (items.Count > 0)
        {
            if (_dialogs.Reconcile(bundle.Profile.Name, items) is not { } answered)
            {
                return;
            }

            choices = answered;
        }

        ApplyImport(bundle, items, choices);
    }

    /// <summary>Adds the reconciled profile and journeys in one undoable step.</summary>
    private void ApplyImport(ProfileBundle bundle, IReadOnlyList<ReconcileItem> items, IReadOnlyList<ReconcileChoice> choices)
    {
        (AgentProfileTemplate profile, List<JourneyTemplate> journeys) = ProfileReconciler.Apply(_study, bundle, items, choices);
        _study.Library.Journeys ??= [];
        IUndoableAction[] actions =
        [
            new AddItemsAction<AgentProfileTemplate>("Import profile", _study.Library.AgentProfiles, [profile], null),
            new AddItemsAction<JourneyTemplate>("Import journeys", _study.Library.Journeys, journeys, null),
        ];
        Execute(new CompositeAction($"Import profile {profile.Name}", actions, OnModelChanged));
        Ticker = $"IMPORTED ▸ {profile.Name} and {journeys.Count} journey{(journeys.Count == 1 ? string.Empty : "s")} ▸ pick it on a spawn zone's mix line.";
    }

    private static string SafeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new([.. name.Select(c => invalid.Contains(c) ? '-' : c)]);
        return string.IsNullOrWhiteSpace(safe) ? "Profile" : safe.Trim();
    }
}
