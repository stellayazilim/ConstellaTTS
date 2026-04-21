using ConstellaTTS.SDK.History;

namespace ConstellaTTS.SDK.UI.Navigation;

/// <summary>
/// History entry for a navigation operation.
/// On rollback, re-applies the inverse request that was captured before the operation executed.
/// </summary>
public sealed class NavigationHistoryEntry(
    INavigationManager navigationManager,
    NavigationRequest rollbackRequest) : IHistoryEntry
{
    /// <inheritdoc/>
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public string Name { get; } = "Navigation";

    /// <inheritdoc/>
    public void Rollback(params object[] args) =>
        navigationManager.Navigate(rollbackRequest);
}
