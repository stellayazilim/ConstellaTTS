using Avalonia.Controls;
using ConstellaTTS.Core.ViewModels;

namespace ConstellaTTS.Core.Views;

public partial class SampleLibraryView : UserControl
{
    public SampleLibraryView(SampleLibraryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
