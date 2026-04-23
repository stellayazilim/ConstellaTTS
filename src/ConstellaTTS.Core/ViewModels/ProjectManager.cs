using System.Collections.ObjectModel;
using ConstellaTTS.Domain;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Manages the active project — owns the track collection and project-level state.
/// Singleton. Exposes observable collections for UI binding.
/// Real load/save/add/remove operations will be added here.
/// </summary>
public sealed class ProjectManager
{
    public ObservableCollection<TrackViewModel> Tracks { get; } = [];

    public ProjectManager()
    {
        // Static placeholder tracks — replaced by real project load later
        var placeholders = new[]
        {
            new Track { Id = 0, Name = "Narrator",   Color = "#7C6AF7" },
            new Track { Id = 1, Name = "Karakter A", Color = "#60AAFF" },
            new Track { Id = 2, Name = "Karakter B", Color = "#FF60A0" },
        };

        foreach (var t in placeholders)
        {
            var vm = new TrackViewModel(t);

            // Dummy sections per track
            if (t.Id == 0)
            {
                vm.Sections.Add(new DummySectionViewModel { Label = "Giriş metni...",     Color = "#7C6AF7", LeftPx = 20,  WidthPx = 160 });
                vm.Sections.Add(new DummySectionViewModel { Label = "Bölüm I açılışı...", Color = "#7C6AF7", LeftPx = 380, WidthPx = 280 });
            }
            else if (t.Id == 1)
            {
                vm.Sections.Add(new DummySectionViewModel { Label = "Dialog...",           Color = "#60AAFF", LeftPx = 200, WidthPx = 180 });
            }
            else if (t.Id == 2)
            {
                vm.Sections.Add(new DummySectionViewModel { Label = "Cevap...",            Color = "#FF60A0", LeftPx = 520, WidthPx = 200 });
            }

            Tracks.Add(vm);
        }
    }
}
