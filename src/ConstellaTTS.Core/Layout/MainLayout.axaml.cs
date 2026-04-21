using Avalonia.Controls;

namespace ConstellaTTS.Core.Layout;

public partial class MainLayout : UserControl
{
    public MainLayout()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Called by TestPluginSimulator (and eventually real plugins) to mount
    /// toolbar controls. Deferred until the layout is attached to the visual tree.
    /// </summary>
    public void AddToToolbar(Control control)
    {
        var slot = this.FindControl<StackPanel>("ToolbarSlot");
        if (slot is not null)
        {
            slot.Children.Add(control);
            return;
        }

        // Not yet attached — defer until visual tree is ready
        void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
        {
            this.AttachedToVisualTree -= OnAttached;
            var s = this.FindControl<StackPanel>("ToolbarSlot");
            s?.Children.Add(control);
        }

        this.AttachedToVisualTree += OnAttached;
    }
}
