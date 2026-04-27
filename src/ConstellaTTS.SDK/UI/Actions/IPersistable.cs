using ConstellaTTS.SDK.Projects;

namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Contract for actions that know how to write their effect into
/// the project. The action carries the data (which section, which
/// sample, which name); when the call site is ready to commit it,
/// it invokes <see cref="Persist"/> and the action mutates the
/// project (and optionally flushes) from inside.
///
/// <para>
/// <b>Action knows what to write, caller knows when.</b> Splitting
/// the two roles keeps each focused: the action holds the payload
/// and understands how it maps to project state (e.g. "this is an
/// AssignSample action — call <c>section.SetSample(sample)</c>"),
/// the caller orchestrates the broader sequence (run the action,
/// persist, push to history, in the right order). Neither side
/// needs to know more than its own half.
/// </para>
///
/// <para>
/// <b>Why <see cref="IProjectManager"/> rather than the project
/// directly.</b> The manager is what's available at every plausible
/// call site (it's the DI-resolved service the view-models hold),
/// it exposes the active project through <c>manager.Active</c> for
/// the mutation, and it owns <c>SaveAsync</c> for actions that
/// want to flush as part of their persist step. Handing the action
/// the manager rather than the project lets each action choose:
/// mutate-only (caller flushes later), or mutate-then-flush
/// (action is self-contained). Both shapes share one signature.
/// </para>
///
/// <para>
/// <b>No default body, by design.</b> A default no-op body would
/// let an action declare itself <see cref="IPersistable"/> and
/// silently do nothing — which is exactly the failure mode the
/// interface exists to prevent. Forcing every implementer to
/// supply a real body means a glance at the action tells you what
/// it persists, and a forgotten override is a compile error
/// rather than a missing manifest write nobody notices until
/// after a reload.
/// </para>
///
/// <para>
/// <b>Orthogonal to <see cref="IReversible"/>.</b> Most persistable
/// actions are also reversible (undo restores the prior project
/// state), but the two interfaces are independent: an action can
/// be persistable without being reversible (a one-way migration),
/// or reversible without being persistable (a UI-only toggle that
/// the manifest doesn't track). The history stack and the project
/// inspect them at different points and never look at each other.
/// </para>
///
/// <para>
/// <b>What about side effects on the filesystem.</b> Actions that
/// do heavier work — copying a sample file into <c>samples/</c>,
/// transcoding an upload, writing a recording — do that work in
/// their <c>ExecuteAsync</c>. <see cref="Persist"/> is purely the
/// in-memory project edit (and optional flush) that goes into
/// <c>project.ctts</c>. An action can use both: <c>ExecuteAsync</c>
/// puts the WAV on disk, <see cref="Persist"/> registers a
/// reference to it on the project. An action that has nothing to
/// write to the filesystem (assigning a sample to a section,
/// renaming a track) leaves <c>ExecuteAsync</c> as a no-op and
/// only implements <see cref="Persist"/>.
/// </para>
///
/// <example>
/// <code>
/// // Mutate-only action — caller is responsible for the flush:
/// public sealed class AddSampleLibraryAction : AsyncActionBase, IReversible, IPersistable
/// {
///     private readonly string _path;
///     public AddSampleLibraryAction(string path) { _path = path; }
///
///     public override Task ExecuteAsync(object? data) => Task.CompletedTask;
///
///     public Task Persist(IProjectManager manager)
///     {
///         manager.Active!.AddSampleLibrary(_path);
///         return Task.CompletedTask;
///     }
///
///     public void Reverse() => /* ... */;
/// }
///
/// // Caller — orchestrates the sequence:
/// public async Task AddLibraryAsync(string path)
/// {
///     var action = new AddSampleLibraryAction(path);
///     await action.ExecuteAsync(null);
///     await action.Persist(_projectManager);   // mutate the live project
///     await _projectManager.SaveAsync();       // flush to disk
///     _history.Push(action);                   // (separately, if reversible)
/// }
///
/// // Self-flushing variant — the action saves as part of persisting:
/// public Task Persist(IProjectManager manager)
/// {
///     manager.Active!.AddSampleLibrary(_path);
///     return manager.SaveAsync();
/// }
/// </code>
/// </example>
/// </summary>
public interface IPersistable
{
    /// <summary>
    /// Apply this action's effect to the active project. Reaches
    /// the project through <c>manager.Active</c>; throws (or
    /// no-ops, at the implementer's discretion) if no project is
    /// active. May or may not also call <see cref="IProjectManager.SaveAsync"/>
    /// — that decision belongs to the action, with most
    /// implementations leaving the flush to the caller and a few
    /// self-contained ones doing both in one step.
    /// </summary>
    /// <param name="manager">
    /// The lifecycle service holding the active project. Always
    /// non-null; the call site is responsible for ensuring there's
    /// a manager (always true in normal flow — it's a singleton).
    /// </param>
    Task Persist(IProjectManager manager);
}
