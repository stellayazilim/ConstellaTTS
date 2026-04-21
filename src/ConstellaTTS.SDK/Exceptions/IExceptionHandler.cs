namespace ConstellaTTS.SDK.Exceptions;

/// <summary>
/// Handles normalized application exceptions.
/// Services throw ConstellaException — the handler decides what to do:
/// log, show UI notification, attempt recovery, etc.
///
/// Unhandled or unnormalizable exceptions bubble up normally and are not passed here.
/// </summary>
public interface IExceptionHandler
{
    /// <summary>
    /// Handles a normalized application exception.
    /// Implementations should never throw — swallow or re-route internally.
    /// </summary>
    void Handle(ConstellaException ex);
}
