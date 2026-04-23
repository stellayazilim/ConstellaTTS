using CommunityToolkit.Mvvm.ComponentModel;

namespace ConstellaTTS.Core.ViewModels;

public sealed partial class SampleLibraryViewModel : ObservableObject
{

    [ObservableProperty]
    private string _searchText = string.Empty;
}
