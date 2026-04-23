namespace ConstellaTTS.SDK.App;

/// <summary>
/// Runs the initial navigation chain to open the first window and mount slots.
/// Designed for one-time use — dispose after BootstrapAsync completes.
/// </summary>
public interface IConstellaBootstrap : IAsyncDisposable
{
    Task BootstrapAsync(CancellationToken cancellationToken = default);
}
