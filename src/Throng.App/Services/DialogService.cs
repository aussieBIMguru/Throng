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

    /// <summary>Asks which levels to use (Copy to Levels, D-060); null when cancelled.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Explanation above the checklist.</param>
    /// <param name="levels">Levels to offer, in display order.</param>
    IReadOnlyList<Guid>? PickLevels(string title, string message, IReadOnlyList<LevelOption> levels);
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
    public IReadOnlyList<Guid>? PickLevels(string title, string message, IReadOnlyList<LevelOption> levels) =>
        LevelPickerDialog.Show(_owner, title, message, levels);
}
