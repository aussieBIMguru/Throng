using System.Windows;
using Microsoft.Win32;
using Throng.App.ViewModels;
using Throng.App.Views;
using Throng.IO;

namespace Throng.App.Services;

/// <summary>Dialogs the view model needs, kept behind an interface so the view model stays UI-agnostic.</summary>
public interface IDialogService
{
    /// <summary>Asks for a study to open; null when cancelled.</summary>
    string? PickOpenPath();

    /// <summary>Asks where to save; null when cancelled.</summary>
    string? PickSavePath(string suggestedFileName);

    /// <summary>Asks whether to save changes: true = save, false = discard, null = cancel.</summary>
    bool? AskSaveChanges(string studyName);

    /// <summary>Shows a problem the user needs to know about.</summary>
    void ShowError(string message);

    /// <summary>Shows an informational message.</summary>
    void ShowInfo(string title, string message);

    /// <summary>Asks for a PNG or JPEG to use as an underlay (D-083); null when cancelled.</summary>
    string? PickImagePath();

    /// <summary>Asks for a folder (results export, D-222); null when cancelled.</summary>
    /// <param name="title">Dialog title.</param>
    string? PickFolder(string title);

    /// <summary>Asks where to save an exported PNG (D-234); null when cancelled.</summary>
    /// <param name="suggestedFileName">File name to start with.</param>
    string? PickPngSavePath(string suggestedFileName);

    /// <summary>Asks which levels to use (Copy to Levels, D-060); null when cancelled.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Explanation above the checklist.</param>
    /// <param name="levels">Levels to offer, in display order.</param>
    IReadOnlyList<Guid>? PickLevels(string title, string message, IReadOnlyList<LevelOption> levels);

    /// <summary>Asks to go ahead with a destructive step; true when confirmed.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">What will happen.</param>
    /// <param name="confirmText">Label of the confirming button, e.g. "DELETE".</param>
    bool Confirm(string title, string message, string confirmText);

    /// <summary>Picks a .throngprofile to import (D-275); null when cancelled.</summary>
    string? PickProfileOpenPath();

    /// <summary>Picks where to export a .throngprofile (D-275); null when cancelled.</summary>
    string? PickProfileSavePath(string suggestedFileName);

    /// <summary>
    /// Asks keep / map / drop for each reference an imported profile makes that the study lacks (D-275); one choice per
    /// item, or null when cancelled.
    /// </summary>
    IReadOnlyList<Throng.Core.Library.ReconcileChoice>? Reconcile(string profileName, IReadOnlyList<Throng.Core.Library.ReconcileItem> items);

    /// <summary>Picks a .throngdevice to import (round 017, D-290); null when cancelled.</summary>
    string? PickDeviceOpenPath();

    /// <summary>Picks where to export a .throngdevice (D-290); null when cancelled.</summary>
    string? PickDeviceSavePath(string suggestedFileName);

    /// <summary>Asks keep / map for an imported device definition's type tag the study doesn't use (D-290); null when cancelled.</summary>
    IReadOnlyList<Throng.Core.Library.ReconcileChoice>? ReconcileDevice(string deviceName, IReadOnlyList<Throng.Core.Library.ReconcileItem> items);

    /// <summary>A themed checklist of items (round 020: what to EXPORT from your library); the ticked ids, or null when cancelled.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Explanation above the checklist.</param>
    /// <param name="items">Items to offer, in display order.</param>
    /// <param name="okText">Label of the confirming button.</param>
    IReadOnlyList<Guid>? PickItems(string title, string message, IReadOnlyList<LevelOption> items, string okText);

    /// <summary>Asks for one line of text (round 020: RENAME, D-345); null when cancelled.</summary>
    string? AskText(string title, string message, string initial, string okText);

    /// <summary>Picks where to export a .thronglib (D-347); null when cancelled.</summary>
    string? PickLibrarySavePath(string suggestedFileName);

    /// <summary>Picks a .thronglib to import into your library (D-347); null when cancelled.</summary>
    string? PickLibraryOpenPath();

    /// <summary>Asks keep both / skip / replace for each .thronglib item your library holds differently (D-347); null when cancelled.</summary>
    IReadOnlyList<Throng.Core.Library.ReconcileChoice>? ReconcileLibrary(string fileName, IReadOnlyList<Throng.Core.Library.ReconcileItem> items);
}

/// <summary>
/// WPF implementation of <see cref="IDialogService"/>. Messages use the themed <see cref="ThrongDialog"/> (D-029);
/// file pickers stay the Windows common dialogs, which users expect and which follow the system theme.
/// </summary>
public sealed class DialogService : IDialogService
{
    private static readonly string Filter = $"Throng study (*{ThrongFile.Extension})|*{ThrongFile.Extension}|All files (*.*)|*.*";
    private readonly Window _owner;

    /// <summary>Creates the service with an owner window for modality.</summary>
    public DialogService(Window owner) => _owner = owner;

    /// <inheritdoc />
    public string? PickOpenPath()
    {
        var dialog = new OpenFileDialog { Filter = Filter, Title = "Open study", CheckFileExists = true };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickSavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = Filter,
            Title = "Save study",
            FileName = suggestedFileName,
            DefaultExt = ThrongFile.Extension,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickImagePath()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Plan images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
            Title = "Add underlay image",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog(_owner) == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public string? PickPngSavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*",
            Title = "Export PNG",
            FileName = suggestedFileName,
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public bool? AskSaveChanges(string studyName)
    {
        int choice = ThrongDialog.ShowMessage(
            _owner,
            "Unsaved changes",
            $"Save changes to '{studyName}'?",
            DialogTone.Question,
            [new DialogButton("SAVE", IsDefault: true), new DialogButton("DON'T SAVE"), new DialogButton("CANCEL", IsCancel: true)]);
        return choice switch
        {
            0 => true,
            1 => false,
            _ => null,
        };
    }

    /// <inheritdoc />
    public void ShowError(string message) =>
        ThrongDialog.ShowMessage(_owner, "Throng", message, DialogTone.Warning, [new DialogButton("OK", IsDefault: true, IsCancel: true)]);

    /// <inheritdoc />
    public void ShowInfo(string title, string message) =>
        ThrongDialog.ShowMessage(_owner, title, message, DialogTone.Info, [new DialogButton("OK", IsDefault: true, IsCancel: true)]);

    /// <inheritdoc />
    public bool Confirm(string title, string message, string confirmText) =>
        ThrongDialog.ShowMessage(
            _owner,
            title,
            message,
            DialogTone.Warning,
            [new DialogButton(confirmText), new DialogButton("CANCEL", IsDefault: true, IsCancel: true)]) == 0;

    /// <inheritdoc />
    public IReadOnlyList<Guid>? PickLevels(string title, string message, IReadOnlyList<LevelOption> levels) =>
        LevelPickerDialog.Show(_owner, title, message, levels);

    /// <inheritdoc />
    public string? PickProfileOpenPath()
    {
        var dialog = new OpenFileDialog { Filter = ProfileFilter, Title = "Import profile", CheckFileExists = true };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickProfileSavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = ProfileFilter,
            Title = "Export profile",
            FileName = suggestedFileName,
            DefaultExt = ProfileFile.Extension,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<Throng.Core.Library.ReconcileChoice>? Reconcile(string profileName, IReadOnlyList<Throng.Core.Library.ReconcileItem> items) =>
        ReconcileDialog.Show(_owner, profileName, items);

    /// <inheritdoc />
    public string? PickDeviceOpenPath()
    {
        var dialog = new OpenFileDialog { Filter = DeviceFilter, Title = "Import device", CheckFileExists = true };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickDeviceSavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = DeviceFilter,
            Title = "Export device",
            FileName = suggestedFileName,
            DefaultExt = DeviceFile.Extension,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<Throng.Core.Library.ReconcileChoice>? ReconcileDevice(string deviceName, IReadOnlyList<Throng.Core.Library.ReconcileItem> items) =>
        ReconcileDialog.Show(_owner, deviceName, items, isDevice: true);

    /// <inheritdoc />
    public IReadOnlyList<Guid>? PickItems(string title, string message, IReadOnlyList<LevelOption> items, string okText) =>
        LevelPickerDialog.Show(_owner, title, message, items, okText);

    /// <inheritdoc />
    public string? AskText(string title, string message, string initial, string okText) =>
        TextPromptDialog.Show(_owner, title, message, initial, okText);

    /// <inheritdoc />
    public string? PickLibrarySavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = LibraryFilter,
            Title = "Export my library",
            FileName = suggestedFileName,
            DefaultExt = LibraryFile.Extension,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickLibraryOpenPath()
    {
        var dialog = new OpenFileDialog { Filter = LibraryFilter, Title = "Import into my library", CheckFileExists = true };
        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<Throng.Core.Library.ReconcileChoice>? ReconcileLibrary(string fileName, IReadOnlyList<Throng.Core.Library.ReconcileItem> items) =>
        ReconcileDialog.Show(_owner, fileName, items);

    private const string LibraryFilter = "Throng library (*.thronglib)|*.thronglib|All files (*.*)|*.*";
    private const string ProfileFilter = "Throng profile (*.throngprofile)|*.throngprofile|All files (*.*)|*.*";
    private const string DeviceFilter = "Throng device (*.throngdevice)|*.throngdevice|All files (*.*)|*.*";
}
