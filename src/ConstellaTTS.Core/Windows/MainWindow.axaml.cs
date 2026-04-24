using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ConstellaTTS.Core.ViewModels;

namespace ConstellaTTS.Core.Windows;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel vm)
    {
        InitializeComponent();
        SetupTitleBar();
        SetupResizeGrips();
        DataContext = vm;
    }

    private void SetupTitleBar()
    {
        var dragArea = this.FindControl<Avalonia.Controls.Border>("TitleBarDragArea");
        if (dragArea is not null)
            dragArea.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    BeginMoveDrag(e);
            };

        var minimize = this.FindControl<Button>("MinimizeBtn");
        if (minimize is not null)
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;

        var maximize = this.FindControl<Button>("MaximizeBtn");
        if (maximize is not null)
            maximize.Click += (_, _) => WindowState =
                WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;

        var close = this.FindControl<Button>("CloseBtn");
        if (close is not null)
            close.Click += (_, _) => Close();
    }

    /// <summary>
    /// Wires each edge/corner hit zone to the appropriate <see cref="WindowEdge"/>
    /// so left-click-drag initiates an OS-native resize. While maximized the
    /// grips are no-ops — user must un-maximize first, matching standard apps.
    /// </summary>
    private void SetupResizeGrips()
    {
        HookGrip("ResizeNW", WindowEdge.NorthWest);
        HookGrip("ResizeN",  WindowEdge.North);
        HookGrip("ResizeNE", WindowEdge.NorthEast);
        HookGrip("ResizeW",  WindowEdge.West);
        HookGrip("ResizeE",  WindowEdge.East);
        HookGrip("ResizeSW", WindowEdge.SouthWest);
        HookGrip("ResizeS",  WindowEdge.South);
        HookGrip("ResizeSE", WindowEdge.SouthEast);
    }

    private void HookGrip(string controlName, WindowEdge edge)
    {
        var grip = this.FindControl<Avalonia.Controls.Border>(controlName);
        if (grip is null) return;

        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (WindowState == WindowState.Maximized) return;
            BeginResizeDrag(edge, e);
        };
    }
}
