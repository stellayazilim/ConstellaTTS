namespace ConstellaTTS.SDK.UI.Navigation;

/// <summary>
/// Orchestrates navigation — opens and closes windows, swaps layouts, mounts and unmounts views.
/// Does not store state; previous states are captured by IHistoryManager before each operation.
/// </summary>
public interface INavigationManager
{
    /// <summary>
    /// Applies a navigation request and pushes a rollback entry to history.
    /// </summary>
    void Navigate(NavigationRequest request);

    /// <summary>
    /// Builds and applies a navigation request using the fluent builder.
    /// </summary>
    void Navigate(Action<NavigationBuilder> configure);
}
