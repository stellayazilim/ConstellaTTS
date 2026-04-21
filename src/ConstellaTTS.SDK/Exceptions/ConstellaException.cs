namespace ConstellaTTS.SDK.Exceptions;

/// <summary>Base class for all normalized ConstellaTTS exceptions.</summary>
public abstract class ConstellaException : Exception
{
    protected ConstellaException(string message) : base(message) { }
    protected ConstellaException(string message, Exception inner) : base(message, inner) { }
}
