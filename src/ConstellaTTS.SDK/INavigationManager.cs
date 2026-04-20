namespace ConstellaTTS.SDK;

/// <summary>
/// Orchestrates navigation — opens/closes windows, swaps layouts, mounts views to slots.
/// Does not store state; previous states live in <see cref="IHistoryManager"/>.
/// </summary>
public interface INavigationManager
{
    /// <summary>
    /// Applies a navigation request and pushes a rollback entry to history.
    /// Use the fluent builder for ergonomic navigation.
    /// </summary>
    void Navigate(NavigationRequest request);

    /// <summary>
    /// Fluent navigation builder.
    /// </summary>
    void Navigate(Action<NavigationBuilder> configure);
}
