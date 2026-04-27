using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace ConstellaTTS.Core.Windows;

/// <summary>
/// Modal prompt for the name to give a freshly recorded sample.
/// Single text input with OK / Cancel; Enter commits, Escape
/// cancels. Owns no view-model — the dialog is one-shot input,
/// the caller hands it a default name (timestamped) and gets a
/// trimmed string back or <c>null</c> on cancel.
///
/// <para>
/// <b>Why a dedicated dialog instead of reusing AddTrackDialog.</b>
/// They render almost identically today, but the title, hint text,
/// confirm-button label and (eventually) per-dialog validation
/// differ. Keeping them as separate types means each one can grow
/// in its own direction (preview waveform here, track-colour
/// picker there) without one's UI bleeding into the other's
/// callers.
/// </para>
/// </summary>
public partial class SampleNameDialog : Window
{
    /// <summary>
    /// Captured on commit. Null until the user confirms; stays null
    /// if they cancel, close via Escape, or submit an empty name.
    /// </summary>
    public string? Result { get; private set; }

    public SampleNameDialog(string defaultName)
    {
        InitializeComponent();

        NameBox.Text = defaultName;

        // Auto-focus + select-all so Enter immediately commits the
        // default name and type-to-replace is the natural edit
        // gesture. Must run after layout so the TextBox is fully
        // hooked up; Dispatcher.Post defers to the next tick.
        Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Loaded);

        ConfirmButton.Click += (_, _) => Commit();
        CancelButton.Click  += (_, _) => Close();

        // Escape and Enter explicit on the window so they work even
        // if focus is in the TextBox. We deliberately don't use
        // IsDefault on Confirm — Avalonia's Fluent theme paints
        // :isDefault buttons with the system accent, overriding
        // our .ctts.accent class.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Result = null;
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
    }

    private void Commit()
    {
        var text = NameBox.Text?.Trim();

        // Empty input behaves as cancel rather than producing a
        // nameless file — keeps the upload pipeline downstream from
        // having to special-case empty stems.
        Result = string.IsNullOrEmpty(text) ? null : text;
        Close();
    }

    /// <summary>
    /// Opens the dialog modally over <paramref name="owner"/> and
    /// returns the typed name, or <c>null</c> if the user cancelled.
    /// </summary>
    public static async Task<string?> ShowAsync(Window owner, string defaultName)
    {
        var dialog = new SampleNameDialog(defaultName);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }
}
