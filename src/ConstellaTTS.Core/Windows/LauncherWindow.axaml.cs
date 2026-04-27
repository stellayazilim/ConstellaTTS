using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using ConstellaTTS.Core.ViewModels;

namespace ConstellaTTS.Core.Windows;

/// <summary>
/// First window the user sees on launch. Hosts brand identity, the
/// "Open" / "New" project entry points, and the recent-projects list.
/// Most logic lives in <see cref="LauncherWindowViewModel"/>; this
/// code-behind only handles platform-specific concerns that don't
/// belong in a view-model:
///
/// <list type="bullet">
///   <item><b>Custom titlebar drag and window controls.</b> Same
///   pattern as <see cref="MainWindow"/>; the launcher uses
///   <c>WindowDecorations="None"</c> so we re-implement title-bar
///   drag and close/minimize ourselves.</item>
///   <item><b>Reveal in folder.</b> A platform shell-out
///   (<c>explorer /select,...</c> on Windows, <c>open -R</c> on
///   macOS, <c>xdg-open</c> on Linux). The view-model raises
///   <see cref="LauncherWindowViewModel.RevealInFolderRequested"/>;
///   this layer subscribes and performs the actual process launch.</item>
///   <item><b>Keybinds.</b> Ctrl+O / Ctrl+N invoke the same commands
///   the action cards do.</item>
/// </list>
/// </summary>
public partial class LauncherWindow : Window
{
    private readonly LauncherWindowViewModel _vm;

    public LauncherWindow(LauncherWindowViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;

        SetupTitleBar();
        SetupKeybinds();

        _vm.RevealInFolderRequested += OnRevealInFolderRequested;
    }

    /// <summary>
    /// Wires the title-bar drag area and the minimize/close buttons.
    /// Mirror of <see cref="MainWindow.SetupTitleBar"/>; the launcher
    /// has no maximize button (window is fixed-size) and no menu.
    /// </summary>
    private void SetupTitleBar()
    {
        var dragArea = this.FindControl<Border>("TitleBarDragArea");
        if (dragArea is not null)
            dragArea.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    BeginMoveDrag(e);
            };

        var minimize = this.FindControl<Button>("MinimizeBtn");
        if (minimize is not null)
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;

        var close = this.FindControl<Button>("CloseBtn");
        if (close is not null)
            close.Click += (_, _) => Close();
    }

    /// <summary>
    /// Ctrl+O opens an existing project, Ctrl+N starts a new one. Both
    /// commands take the window itself as the visual reference so the
    /// view-model can resolve <see cref="TopLevel"/> and pop a native
    /// folder picker.
    /// </summary>
    private void SetupKeybinds()
    {
        AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: false);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control) return;

        switch (e.Key)
        {
            case Key.O:
                e.Handled = true;
                await _vm.OpenProjectCommand.ExecuteAsync(this);
                break;

            case Key.N:
                e.Handled = true;
                await _vm.NewProjectCommand.ExecuteAsync(this);
                break;
        }
    }

    /// <summary>
    /// Handles the view-model's reveal-in-folder request. The VM
    /// already updated its status text; this layer takes care of
    /// the actual platform shell-out.
    /// </summary>
    private void OnRevealInFolderRequested(object? sender, string path) =>
        TryRevealInFolder(path);

    /// <summary>
    /// Cross-platform "reveal file/folder in OS file manager".
    /// Best-effort: failures (path missing, OS unsupported, no shell)
    /// are swallowed because this is a convenience action — the user
    /// can still open the project, the reveal is just a nicety.
    /// </summary>
    private static void TryRevealInFolder(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "explorer.exe",
                    Arguments       = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "open",
                    Arguments       = $"-R \"{path}\"",
                    UseShellExecute = true,
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // xdg-open lacks a "select" mode; fall back to opening
                // the parent directory.
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Process.Start(new ProcessStartInfo
                    {
                        FileName        = "xdg-open",
                        Arguments       = $"\"{dir}\"",
                        UseShellExecute = true,
                    });
            }
        }
        catch
        {
            // Best-effort — silently ignore. The status text in the
            // view-model has already given the user feedback that the
            // click was received.
        }
    }
}
