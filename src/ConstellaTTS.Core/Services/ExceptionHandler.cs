using System.Diagnostics;
using ConstellaTTS.SDK.Exceptions;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// Default IExceptionHandler implementation.
/// Logs to debug output and raises ExceptionHandled so the UI layer can react
/// (show notification, trigger recovery, etc.) without coupling to the handler directly.
/// </summary>
public sealed class ExceptionHandler : IExceptionHandler
{
    /// <summary>
    /// Raised after every handled exception.
    /// UI layer subscribes here to show notifications or trigger recovery flows.
    /// </summary>
    public event Action<ConstellaException>? ExceptionHandled;

    public void Handle(ConstellaException ex)
    {
        Debug.WriteLine($"[ExceptionHandler] {ex.GetType().Name}: {ex.Message}");

        if (ex.InnerException is not null)
            Debug.WriteLine($"[ExceptionHandler] caused by: {ex.InnerException.Message}");

        ExceptionHandled?.Invoke(ex);
    }

   
}
