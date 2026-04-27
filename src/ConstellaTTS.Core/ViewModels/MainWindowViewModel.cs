using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.ViewModelContracts;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// View-model for <see cref="Windows.MainWindow"/>. Single-document
/// model — exposes the <see cref="IConstellaProject"/> the launcher
/// (or a future open-by-cli flow) handed over via the navigation
/// payload, so the DAW view can bind to its members directly
/// (<c>{Binding ConstellaProject.Name}</c>,
/// <c>{Binding ConstellaProject.Path}</c>, …).
///
/// <para>
/// <b>Not observable.</b> The project property is a plain getter, not
/// an <c>[ObservableProperty]</c>. Once a project is opened the DAW
/// runs against it for the lifetime of the window — the same launcher
/// is used to switch projects, which closes <see cref="Windows.MainWindow"/>
/// before opening the next, so we never need to mutate
/// <see cref="ConstellaProject"/> in place. The property is here as a
/// seed for the rest of the wiring (other services that need to
/// initialize against the project) and as a binding target for the
/// view, nothing more.
/// </para>
///
/// <para>
/// <b>Empty-state.</b> When no project is active (e.g. the DAW is
/// opened directly via <see cref="DawDirectBootstrap"/> for a
/// debugging session, no launcher in the loop), the property stays
/// <c>null</c>. The view is responsible for rendering a sensible
/// fallback in that case — a static title, a "no project" placeholder,
/// etc. The <c>x:DataType</c>-aware bindings will simply produce empty
/// values rather than throwing.
/// </para>
/// </summary>
public partial class MainWindowViewModel : ViewModel
{
    private readonly ILogger _logger;

    /// <summary>
    /// The project the DAW is operating on. Set once via
    /// <see cref="GetParams"/> when the launcher's open-DAW navigation
    /// fires; remains null when no project was supplied.
    /// </summary>
    public IConstellaProject? ConstellaProject { get; private set; }

    public MainWindowViewModel(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("MainWindowViewModel");
    }

    /// <summary>
    /// Receives the navigation payload from the launcher → DAW
    /// transition. Expects an <see cref="IConstellaProject"/>; any
    /// other payload (or null) leaves <see cref="ConstellaProject"/>
    /// untouched. After updating the seed property, calls into
    /// <see cref="ViewModel.GetParams"/> so the blanket
    /// PropertyChanged flushes any binding pointing at
    /// <c>ConstellaProject.*</c>.
    /// </summary>
    public override void GetParams(object? data)
    {
        if (data is IConstellaProject project)
        {
            ConstellaProject = project;
            _logger.LogInformation(
                "MainWindow opened with project: {Name} at {Path}",
                project.Name, project.Path);
        }

        base.GetParams(data);
    }
}
