using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.SDK.UI.Navigation;

/// <summary>
/// Base record for all navigation requests — pure data.
/// Implements IAction for MVVM command binding.
/// Implements IReversible — Reverse() returns the inverse NavigationRequest.
/// </summary>
public abstract record NavigationRequest : IAction, IReversible
{
    public abstract string  Id          { get; }
    public abstract string  Name        { get; }
    public virtual  string? Description => null;

    public abstract void    Execute(object? data = null);
    public abstract IAction Reverse(IReversible? previous, params object[] args);

    public virtual bool CanExecute(object? parameter) => true;
    void System.Windows.Input.ICommand.Execute(object? parameter) => Execute(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

public sealed record OpenWindowRequest(Type WindowType) : NavigationRequest
{
    public override string  Id   => $"OpenWindow:{WindowType.Name}";
    public override string  Name => $"Pencere Aç: {WindowType.Name}";
    public override void    Execute(object? data = null) { }
    public override IAction Reverse(IReversible? previous, params object[] args) => new CloseWindowRequest(WindowType);
}

public sealed record CloseWindowRequest(Type WindowType) : NavigationRequest
{
    public override string  Id   => $"CloseWindow:{WindowType.Name}";
    public override string  Name => $"Pencere Kapat: {WindowType.Name}";
    public override void    Execute(object? data = null) { }
    public override IAction Reverse(IReversible? previous, params object[] args) => new OpenWindowRequest(WindowType);
}

public sealed record ShowFlyoutRequest(Type FlyoutType) : NavigationRequest
{
    public override string  Id   => $"ShowFlyout:{FlyoutType.Name}";
    public override string  Name => $"Panel Aç: {FlyoutType.Name}";
    public override void    Execute(object? data = null) { }
    public override IAction Reverse(IReversible? previous, params object[] args) => new HideFlyoutRequest(FlyoutType);
}

public sealed record HideFlyoutRequest(Type FlyoutType) : NavigationRequest
{
    public override string  Id   => $"HideFlyout:{FlyoutType.Name}";
    public override string  Name => $"Panel Kapat: {FlyoutType.Name}";
    public override void    Execute(object? data = null) { }
    public override IAction Reverse(IReversible? previous, params object[] args) => new ShowFlyoutRequest(FlyoutType);
}

public sealed record MountRegionRequest(string RegionId, Type ViewType) : NavigationRequest
{
    public override string  Id   => $"MountRegion:{RegionId}";
    public override string  Name => $"Region Mount: {RegionId}";
    public override void    Execute(object? data = null) { }
    public override IAction Reverse(IReversible? previous, params object[] args) => new UnmountRegionRequest(RegionId);
}

public sealed record UnmountRegionRequest(string RegionId) : NavigationRequest
{
    public override string  Id   => $"UnmountRegion:{RegionId}";
    public override string  Name => $"Region Unmount: {RegionId}";
    public override void    Execute(object? data = null) { }
    public override IAction Reverse(IReversible? previous, params object[] args) => this;
}

public sealed record QueueNavigationRequest(IReadOnlyList<NavigationRequest> Requests) : NavigationRequest
{
    public override string  Id   => "QueueNavigation";
    public override string  Name => "Toplu Navigasyon";
    public override void    Execute(object? data = null) { }
    public override IAction Reverse(IReversible? previous, params object[] args) =>
        new QueueNavigationRequest(Requests.Select(r => (NavigationRequest)r.Reverse(null)).Reverse().ToList());
}
