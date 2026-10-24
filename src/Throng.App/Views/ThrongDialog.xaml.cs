using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Throng.App.Views;

/// <summary>Tone of a <see cref="ThrongDialog"/>: sets the frame colour and title glyph.</summary>
public enum DialogTone
{
    /// <summary>Informational (cyan, info glyph).</summary>
    Info,

    /// <summary>A question that needs an answer (cyan, help glyph).</summary>
    Question,

    /// <summary>Something went wrong (red, warning glyph).</summary>
    Warning,
}

/// <summary>One button on a <see cref="ThrongDialog"/>.</summary>
/// <param name="Label">Button text (VT323 caps reads best).</param>
/// <param name="IsDefault">Triggered by Enter.</param>
/// <param name="IsCancel">Triggered by Esc and by closing the dialog.</param>
public sealed record DialogButton(string Label, bool IsDefault = false, bool IsCancel = false);

/// <summary>
/// Themed modal message dialog that replaces the stock MessageBox (D-029). Use <see cref="ShowMessage"/>.
/// </summary>
public partial class ThrongDialog : Window
{
    private int _result = -1;

    private ThrongDialog() => InitializeComponent();

    /// <summary>
    /// Shows the dialog modally and returns the index of the button pressed. Esc or closing returns the cancel
    /// button's index (or the last button when none is marked as cancel).
    /// </summary>
    /// <param name="owner">Owner window for centring and modality; may be null.</param>
    /// <param name="title">Title strip text.</param>
    /// <param name="message">Body text.</param>
    /// <param name="tone">Colour and glyph.</param>
    /// <param name="buttons">Buttons left to right; at least one.</param>
    public static int ShowMessage(Window? owner, string title, string message, DialogTone tone, IReadOnlyList<DialogButton> buttons)
    {
        var dialog = new ThrongDialog { Title = title };
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
        dialog.ApplyTone(tone);

        IReadOnlyList<DialogButton> list = buttons.Count > 0 ? buttons : [new DialogButton("OK", IsDefault: true, IsCancel: true)];
        int cancelIndex = list.Count - 1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].IsCancel)
            {
                cancelIndex = i;
            }
        }

        for (int i = 0; i < list.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = list[i].Label,
                IsDefault = list[i].IsDefault,
                IsCancel = i == cancelIndex,
                MinWidth = 86,
                Margin = new Thickness(8, 0, 0, 0),
                FontFamily = (System.Windows.Media.FontFamily)dialog.FindResource("RetroFont"),
                FontSize = 16,
            };
            button.Click += (_, _) =>
            {
                dialog._result = index;
                dialog.Close();
            };
            dialog.ButtonPanel.Children.Add(button);
        }

        dialog.TitleStrip.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                dialog.DragMove();
            }
        };

        dialog.ShowDialog();
        return dialog._result < 0 ? cancelIndex : dialog._result;
    }

    private void ApplyTone(DialogTone tone)
    {
        string brushKey = tone == DialogTone.Warning ? "RedBrush" : "AccentBrush";
        var brush = (System.Windows.Media.Brush)FindResource(brushKey);
        Frame.BorderBrush = brush;
        TitleText.Foreground = brush;
        ToneGlyph.Foreground = brush;
        ToneGlyph.Text = tone switch
        {
            DialogTone.Warning => "",
            DialogTone.Question => "",
            _ => "",
        };
    }
}
