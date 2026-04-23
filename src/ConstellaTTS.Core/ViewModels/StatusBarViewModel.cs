namespace ConstellaTTS.Core.ViewModels;

public enum EngineStatus { Idle, Loading, Warming, Generating }

/// <summary>
/// ViewModel for the status bar.
/// Progress bar binding is handled in code-behind when real engine plugin arrives.
/// </summary>
public sealed class StatusBarViewModel
{
    public string       Engine      { get; init; } = "Chatterbox ML";
    public string       Duration    { get; init; } = "dur —";
    public string       BlockCount  { get; init; } = "blocks 4";
    public string       Cache       { get; init; } = "cache 2 / 4";
    public EngineStatus Status      { get; init; } = EngineStatus.Loading;
    public double       Progress    { get; init; } = 0.6;
    public string       StatusLabel { get; init; } = "model loading...";
}
