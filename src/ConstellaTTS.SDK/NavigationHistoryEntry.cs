namespace ConstellaTTS.SDK;

/// <summary>
/// History entry for a navigation operation.
/// Rolls back by re-applying the inverse request captured before the navigation occurred.
/// </summary>
public sealed class NavigationHistoryEntry(
    INavigationManager navigationManager,
    NavigationRequest rollbackRequest) : IHistoryEntry
{
    public string Id   { get; } = Guid.NewGuid().ToString();
    public string Name { get; } = "Navigation";

    public void Rollback(params object[] args) =>
        navigationManager.Navigate(rollbackRequest);
}
