using System.Windows;
using System.Windows.Input;

namespace Throng.App.Views;

/// <summary>
/// Themed modal one-line text prompt (round 020: RENAME in your library, D-345). Use <see cref="Show"/>.
/// </summary>
public partial class TextPromptDialog : Window
{
    private bool _accepted;

    private TextPromptDialog() => InitializeComponent();

    /// <summary>Shows the prompt modally; the entered text, or null when cancelled.</summary>
    /// <param name="owner">Owner window for centring and modality; may be null.</param>
    /// <param name="title">Title strip text.</param>
    /// <param name="message">Explanation above the box.</param>
    /// <param name="initial">Text to start with (selected).</param>
    /// <param name="okText">Label of the confirming button.</param>
    public static string? Show(Window? owner, string title, string message, string initial, string okText)
    {
        var dialog = new TextPromptDialog { Title = title };
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.TitleText.Text = title.ToUpperInvariant();
        dialog.MessageText.Text = message;
        dialog.OkButton.Content = okText;
        dialog.InputBox.Text = initial;
        dialog.Loaded += (_, _) =>
        {
            dialog.InputBox.Focus();
            dialog.InputBox.SelectAll();
        };
        dialog.OkButton.Click += (_, _) =>
        {
            dialog._accepted = true;
            dialog.Close();
        };
        dialog.CancelButton.Click += (_, _) => dialog.Close();
        dialog.TitleStrip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                dialog.DragMove();
            }
        };

        dialog.ShowDialog();
        return dialog._accepted ? dialog.InputBox.Text : null;
    }
}
