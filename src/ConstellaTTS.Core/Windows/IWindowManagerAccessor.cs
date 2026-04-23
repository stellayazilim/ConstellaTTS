using Avalonia.Controls;

namespace ConstellaTTS.Core.Windows;

/// <summary>
/// Thin accessor so SampleLibraryWindow can lazily resolve the main window
/// without taking a hard dependency on IWindowManager (which would create
/// a circular dependency via the DI graph).
/// </summary>
public interface IWindowManagerAccessor
{
    Window GetDefaultWindow();
}
