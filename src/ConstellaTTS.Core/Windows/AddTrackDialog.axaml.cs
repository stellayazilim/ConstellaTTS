
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace ConstellaTTS.Core.Windows;

/// <summary>
/// Modal prompt for a new track's name. Single text input with OK/Cancel;
/// Enter commits, Escape cancels. Opened via <see cref="ShowAsync"/> from
/// any code that can reach the owning <see cref="Window"/>.
///
/// The dialog owns no VM — it's a one-shot input, so plumbing it through
/// MVVM + an IDialogService would cost more than it saves. Callers get
/// <c>null</c> on cancel and the typed string on commit.
/// </summary>
public partial class AddTrackDialog : Window
{
    /// <summary>
    /// Captured on commit. Null until the user confirms; stays null if
    /// they cancel, close via Escape, or click off the OK path.
    /// </summary>
    public string? Result { get; private set; }

    public AddTrackDialog(string defaultName)
    {
        InitializeComponent();

        NameBox.Text = defaultName;

        // Auto-focus + select-all so Enter immediately commits the default
        // name and type-to-replace is the natural edit. Must run after
        // layout so the TextBox is fully hooked up; Dispatcher.Post defers
        // to the next tick.
        Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Loaded);

        ConfirmButton.Click += (_, _) => Commit();
        CancelButton.Click  += (_, _) => Close();

        // Escape is already implicit on IsCancel buttons but we wire it
        // explicitly on the window so the key works even if focus has
        // left the buttons (e.g. user is typing). Enter is also wired
        // manually — we deliberately don't use IsDefault on the Confirm
        // button because Avalonia's Fluent theme paints :isDefault buttons
        // with the system accent (red on Windows by default), which
        // overrides our .ctts.accent styling.
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
        if (string.IsNullOrEmpty(text))
        {
            // Treat empty input as cancel rather than creating a nameless
            // track; matches the "Cancel" button's semantics.
            Result = null;
        }
        else
        {
            Result = text;
        }
        Close();
    }

    /// <summary>
    /// Opens the dialog modally over <paramref name="owner"/> and returns
    /// the typed name, or null if the user cancelled.
    /// </summary>
    public static async Task<string?> ShowAsync(Window owner, string defaultName)
    {
        var dialog = new AddTrackDialog(defaultName);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }
}
