using System.Collections.ObjectModel;
using System.Net.Mime;
using Avalonia.Logging;
using ConstellaTTS.Core.Logging;
using ConstellaTTS.SDK.Primitives;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.ViewModels;

public sealed class TrackListViewModel: ViewModel
{
    private readonly ProjectManager _projectManager;
    public TrackListViewModel(ProjectManager projectManager)
    {
        _projectManager = projectManager;
    }
    
    public ObservableCollection<TrackViewModel> Tracks => _projectManager.Tracks;

    public ContextBarViewModel ContextBar { get; } = new();

    // Dummy chapters — replaced by real project data later
    public ObservableCollection<DummyChapterViewModel> Chapters { get; } =
    [
        new() { Name = "Giriş",   Color = "#7C6AF7", TrackCount = "2 track", Duration = "0:32" },
        new() { Name = "Bölüm I", Color = "#60AAFF", TrackCount = "3 track", Duration = "1:15" },
        new() { Name = "Bölüm II",Color = "#D060FF", TrackCount = "2 track", Duration = "0:58" },
        new() { Name = "Kapanış", Color = "#FF60A0", TrackCount = "1 track", Duration = "0:20" },
    ];
}
