namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// ViewModel for the context bar — shows active section info and UI hints.
/// Populated by ProjectManager when active section changes.
/// </summary>
public sealed class ContextBarViewModel
{
    // Active section summary — e.g. "Giriş · 2 track · 0:32"
    public string SectionLabel { get; init; } = "Giriş · 2 track · 0:32";
    public string BlockCount   { get; init; } = "blocks 4";

    // Static hint text
    public string Hints { get; init; } =
        "Ctrl+Scroll=zoom · Minimap'te seç=fit · Block'a tıkla=editör";
}
