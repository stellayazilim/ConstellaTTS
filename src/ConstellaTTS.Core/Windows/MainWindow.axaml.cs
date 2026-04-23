using Avalonia.Controls;
using ConstellaTTS.Core.ViewModels;

namespace ConstellaTTS.Core.Windows;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel vm)
    {
        InitializeComponent();
        SetupTitleBar();
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
}
