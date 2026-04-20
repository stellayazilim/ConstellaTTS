using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace ConstellaTTS.Core.Controls;

/// <summary>
/// Base class for vector icon controls used inside <see cref="PlayerButton"/>.
/// Inherits Foreground from parent via TemplatedControl.
/// </summary>
public abstract class PlayerIcon : TemplatedControl;
