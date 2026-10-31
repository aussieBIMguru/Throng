using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Services;
using Throng.Core.Common;
using Throng.Core.Library;
using Throng.IO;

namespace Throng.App.ViewModels;

/// <summary>
/// Your library across studies (round 020, D-340–D-351): agent profiles, device definitions and journeys kept in
/// <c>%AppData%\Throng\Library\library.json</c>. LIBRARY lists them inside each tab marked MINE (the study's own first,
/// then MINE, then built-ins, D-343). SAVE TO MY LIBRARY in SELECTION copies the study's version in (asking before it
/// replaces one, D-345), UPDATE FROM MY LIBRARY takes it back into the study (one undo step, D-344), and a MINE item is
/// embedded in the study under its id the first time it's used (D-346). RENAME and DELETE live on LIBRARY's right-click
/// menu; EXPORT / IMPORT exchange a ticked set as a <c>.thronglib</c> (D-347). Library changes don't touch the study, so
/// they're not on its undo stack: each one confirms and keeps the previous file as <c>library.bak.json</c> (D-348).
/// </summary>
public sealed partial class MainViewModel
{
    private UserLibraryStore? _myLibraryStore;
    private RelayCommand? _exportMyLibraryCommand;
    private RelayCommand? _importMyLibraryCommand;

    /// <summary>Your library (MINE) as loaded; empty when it couldn't be read.</summary>
    public TemplateLibrary Mine => _myLibraryStore?.Library ?? EmptyMine;

    /// <summary>LIBRARY ▸ MY LIBRARY ▸ EXPORT…: a ticked set of your items to a .thronglib (D-347).</summary>
    public RelayCommand ExportMyLibraryCommand => _exportMyLibraryCommand ??= new RelayCommand(ExportMyLibrary);

    /// <summary>LIBRARY ▸ MY LIBRARY ▸ IMPORT…: a .thronglib into your library through the reconcile dialog (D-347).</summary>
    public RelayCommand ImportMyLibraryCommand => _importMyLibraryCommand ??= new RelayCommand(ImportMyLibrary);

    /// <summary>Where your library lives, for tooltips and the ticker.</summary>
    public string MyLibraryFolder => _myLibraryStore?.Folder ?? AppPaths.UserLibraryFolder;

    /// <summary>True when your library holds this id (any of its kinds).</summary>
    public bool IsInMine(Guid id) => MyLibrary.Find(Mine, id) is not null;

    /// <summary>True when your library holds this id and the study doesn't (yet).</summary>
    public bool IsMineOnly(Guid id) => IsInMine(id) && _study.Library.Find(id) is null;

    /// <summary>True when the study's copy differs from your library's (D-344, D-351).</summary>
    public bool MineDiffers(Guid id) => MyLibrary.Differs(_study, Mine, id);

    /// <summary>The line SELECTION shows about your library for a definition; empty when it isn't in your library.</summary>
    public string MineStatusText(Guid id)
    {
        if (!IsInMine(id))
        {
            return string.Empty;
        }

        if (IsMineOnly(id))
        {
            return "MINE ▸ from your library. Using or editing it here copies it into this study; your library stays as it is.";
        }

        return MineDiffers(id)
            ? "DIFFERS FROM MY LIBRARY ▸ UPDATE FROM MY LIBRARY takes your library's version (undoable); SAVE TO MY LIBRARY replaces your library's with this one."
            : "MINE ▸ the same as your library's.";
    }

    /// <summary>
    /// LIBRARY row note (round 017, round 020): MINE for your items (and the study's copies of them), DIFFERS when the
    /// study's copy differs from yours, else MODIFIED / IMPORTED / YOURS as before; null for built-ins.
    /// </summary>
    public string? LibraryNote(TemplateBase template)
    {
        if (IsInMine(template.Id))
        {
            return MineDiffers(template.Id) ? "DIFFERS" : "MINE";
        }

        return StudyDefinitions.IsModified(_study, template) ? "MODIFIED"
            : template.Source == TemplateSource.Imported ? "IMPORTED"
            : StudyDefinitions.IsBuiltIn(template) ? null
            : "YOURS";
    }

    /// <summary>
    /// Embeds an item the study is about to use (D-346): your library's copy, else the built-in, under the same id,
    /// outside the undo stack (as placing has always done). Nothing when the study already holds it.
    /// </summary>
    public void EmbedForUse(Guid id)
    {
        if (MyLibrary.EmbedForUse(_study, Mine, id).Count > 0)
        {
            AppLog.Info($"Embedded {id} in the study (first use).");
        }
    }

    /// <summary>
    /// SAVE TO MY LIBRARY (D-342, D-345, D-349, D-350): copies the definition as the study sees it (and a profile's
    /// default journey) into your library, asking first when that replaces one of yours or saves picks.
    /// </summary>
    public void SaveToMyLibrary(TemplateBase template)
    {
        if (!MyLibrary.Holds(template) || _myLibraryStore is null)
        {
            return;
        }

        // The study's version when it has one, else yours (nothing to save), else the built-in shown.
        TemplateBase current = _study.Library.Find(template.Id) ?? MyLibrary.Find(Mine, template.Id) ?? template;
        LibrarySavePlan plan = MyLibrary.PlanSave(_study, Mine, current);
        if (plan.IsUpToDate)
        {
            Ticker = $"MY LIBRARY ▸ {current.Name} is already there with the same values.";
            return;
        }

        var notes = new List<string>();
        if (plan.Replaces.Count > 0)
        {
            notes.Add($"This replaces {Join(plan.Replaces.Select(t => $"'{t.Name}'"))} in your library. Studies that use {(plan.Replaces.Count == 1 ? "it" : "them")} keep their own copy until you UPDATE FROM MY LIBRARY there.");
        }

        if (plan.JourneysWithPicks.Count > 0)
        {
            notes.Add($"{Join(plan.JourneysWithPicks.Select(j => $"'{j.Name}'"))} picks specific devices or zones in this study. Its picks are kept; in other studies, where they match nothing, those nodes use their device type or zone tag instead (CHECK notes it).");
        }

        if (plan.Items.Count > 1)
        {
            notes.Add($"Its default journey '{plan.Items[^1].Name}' is saved with it, so it works in any study.");
        }

        if ((plan.Replaces.Count > 0 || plan.JourneysWithPicks.Count > 0)
            && !_dialogs.Confirm("Save to my library", string.Join(Environment.NewLine + Environment.NewLine, notes), plan.Replaces.Count > 0 ? "REPLACE" : "SAVE"))
        {
            return;
        }

        if (!CommitMine(mine => MyLibrary.ApplySave(mine, plan)))
        {
            return;
        }

        Ticker = plan.Items.Count switch
        {
            1 => $"MY LIBRARY ▸ saved {plan.Items[0].Name}",
            _ => $"MY LIBRARY ▸ saved {plan.Items[0].Name} and its default journey {plan.Items[^1].Name}",
        };
    }

    /// <summary>
    /// UPDATE FROM MY LIBRARY (D-344): your library's values onto the study's copy, through the definition's own edit
    /// path, so it's one undo step and placed devices follow. A profile's default journey comes along when it's missing.
    /// </summary>
    public void UpdateFromMyLibrary(Guid id)
    {
        if (MyLibrary.Find(Mine, id) is not { } saved)
        {
            return;
        }

        string description = $"Update {saved.Name} from my library";
        switch (saved)
        {
            case DeviceTemplate device:
                EditDeviceDefinition(id, description, t => StudyDefinitions.CopyValues(t, device));
                break;

            case AgentProfileTemplate profile:
                if (profile.DefaultJourneyId is { } journeyId)
                {
                    EmbedForUse(journeyId);
                }

                EditProfileDefinition(id, description, t => StudyDefinitions.CopyValues(t, profile));
                break;

            case JourneyTemplate journey:
                EditJourney(id, description, j =>
                {
                    StudyDefinitions.CopyValues(j, journey);
                    return true;
                });
                break;
        }
    }

    /// <summary>LIBRARY right-click ▸ RENAME… on one of your items (D-345).</summary>
    public void RenameInMyLibrary(TemplateBase template)
    {
        if (MyLibrary.Find(Mine, template.Id) is not { } item)
        {
            return;
        }

        string? name = _dialogs.AskText("Rename in my library", $"A new name for '{item.Name}' in your library. Studies that already use it keep their own copy and its name.", item.Name, "RENAME");
        if (name is null || string.Equals(name.Trim(), item.Name, StringComparison.Ordinal))
        {
            return;
        }

        if (name.Trim().Length == 0)
        {
            Ticker = "A library item needs a name.";
            return;
        }

        if (CommitMine(mine => MyLibrary.Rename(mine, item.Id, name)))
        {
            Ticker = $"MY LIBRARY ▸ renamed to {name.Trim()}";
        }
    }

    /// <summary>LIBRARY right-click ▸ DELETE FROM MY LIBRARY (D-345, D-348): confirms; studies keep their copies.</summary>
    public void DeleteFromMyLibrary(TemplateBase template)
    {
        if (MyLibrary.Find(Mine, template.Id) is not { } item)
        {
            return;
        }

        string builtIn = BuiltInLibrary.Instance.Find(item.Id) is not null ? " The built-in it was saved from shows again." : string.Empty;
        if (!_dialogs.Confirm("Delete from my library", $"Delete '{item.Name}' from your library? Studies that use it keep their own copy.{builtIn} There's no undo, but the previous library is kept as {UserLibraryStore.BackupFileName}.", "DELETE"))
        {
            return;
        }

        if (CommitMine(mine => MyLibrary.Remove(mine, item.Id)))
        {
            Ticker = $"MY LIBRARY ▸ deleted {item.Name}";
        }
    }

    private void ExportMyLibrary()
    {
        List<TemplateBase> items =
        [
            .. Mine.AgentProfiles.Cast<TemplateBase>(),
            .. Mine.Devices,
            .. Mine.Journeys ?? [],
        ];
        if (items.Count == 0)
        {
            Ticker = "MY LIBRARY ▸ empty: SAVE TO MY LIBRARY on a profile, device or journey in SELECTION first.";
            return;
        }

        IReadOnlyList<Guid>? ids = _dialogs.PickItems(
            "Export my library",
            "Tick what to share. A profile's default journey goes with it.",
            [.. items.Select(t => new LevelOption(t.Id, $"{MyLibrary.KindName(t)} · {t.Name}"))],
            "EXPORT");
        if (ids is null || ids.Count == 0)
        {
            return;
        }

        string? path = _dialogs.PickLibrarySavePath("My library" + LibraryFile.Extension);
        if (path is null)
        {
            return;
        }

        TemplateLibrary export = MyLibrary.BuildExport(Mine, ids);
        Result saved = LibraryFile.Save(export, path);
        if (!saved.IsSuccess)
        {
            AppLog.Warn(saved.Error ?? "Library export failed.");
            _dialogs.ShowError(saved.Error ?? "The library couldn't be exported.");
            return;
        }

        int count = export.AgentProfiles.Count + export.Devices.Count + (export.Journeys?.Count ?? 0);
        Ticker = string.Create(CultureInfo.InvariantCulture, $"EXPORTED ▸ {count} item{(count == 1 ? string.Empty : "s")} ▸ {Path.GetFileName(path)}");
    }

    private void ImportMyLibrary()
    {
        string? path = _dialogs.PickLibraryOpenPath();
        if (path is null)
        {
            return;
        }

        Result<TemplateLibrary> loaded = LibraryFile.Load(path);
        if (!loaded.IsSuccess || loaded.Value is not { } incoming)
        {
            _dialogs.ShowError(loaded.Error ?? "The library file couldn't be read.");
            return;
        }

        List<ReconcileItem> clashes = MyLibrary.FindImportClashes(Mine, incoming);
        IReadOnlyList<ReconcileChoice> choices = [];
        if (clashes.Count > 0)
        {
            if (_dialogs.ReconcileLibrary(Path.GetFileName(path), clashes) is not { } answered)
            {
                return;
            }

            choices = answered;
        }

        LibraryImportResult? result = null;
        if (!CommitMine(mine => result = MyLibrary.ApplyImport(mine, incoming, choices)) || result is null)
        {
            return;
        }

        var parts = new List<string>();
        if (result.Added > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{result.Added} added"));
        }

        if (result.Replaced > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{result.Replaced} replaced"));
        }

        if (result.Skipped > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{result.Skipped} skipped"));
        }

        if (result.Unchanged > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{result.Unchanged} already there"));
        }

        Ticker = $"IMPORTED INTO MY LIBRARY ▸ {(parts.Count == 0 ? "nothing to add" : string.Join(", ", parts))}";
    }

    /// <summary>Loads your library at start-up (a problem is logged and shown in the ticker; the library starts empty).</summary>
    private void InitialiseMyLibrary()
    {
        _myLibraryStore = new UserLibraryStore(AppPaths.UserLibraryFolder);
        Result loaded = _myLibraryStore.Load();
        if (!loaded.IsSuccess)
        {
            AppLog.Warn(loaded.Error ?? "Your library couldn't be read.");
            Ticker = $"MY LIBRARY ▸ {loaded.Error}";
        }
    }

    /// <summary>
    /// Safety net (D-346): embeds your items the study refers to but lacks, so files and runs never depend on your
    /// library. Called after every model change, before saving and before a run.
    /// </summary>
    private void EmbedReferencedFromMine()
    {
        IReadOnlyList<TemplateBase> added = MyLibrary.EmbedReferenced(_study, Mine);
        if (added.Count > 0)
        {
            AppLog.Info($"Embedded {added.Count} item(s) from your library in the study: {string.Join(", ", added.Select(t => t.Name))}.");
        }
    }

    /// <summary>Changes and saves your library (D-348), then refreshes LIBRARY and SELECTION; false (with an error shown) when it couldn't be saved.</summary>
    private bool CommitMine(Action<TemplateLibrary> change)
    {
        if (_myLibraryStore is null)
        {
            return false;
        }

        Result saved = _myLibraryStore.Update(change);
        if (!saved.IsSuccess)
        {
            AppLog.Warn(saved.Error ?? "Your library couldn't be saved.");
            _dialogs.ShowError(saved.Error ?? "Your library couldn't be saved.");
            return false;
        }

        AppLog.Info($"Saved your library ({_myLibraryStore.FilePath}).");
        RefreshJourneys();
        return true;
    }

    private static string Join(IEnumerable<string> names)
    {
        List<string> list = [.. names];
        return list.Count <= 1 ? string.Concat(list) : $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}";
    }

    private static readonly TemplateLibrary EmptyMine = new();
}
