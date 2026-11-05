using Content.Shared._SW._Senate.Holonet;
using Robust.Shared.Player;

namespace Content.Client._SW._Senate.Holonet;

/// <summary>
/// Delivers the private per-session Holonet state to the views open for its device. Views go blank as soon as
/// the controlled entity changes or the server ends their context, whichever reaches this client first.
/// </summary>
public sealed class SWHolonetClientSystem : EntitySystem
{
    private readonly List<SWHolonetView> _views = new();
    private ulong _lastEpoch;

    /// <summary>
    /// Shared by all views: the server keeps a viewer for a moment after its window closes, and a reopened
    /// window must still count upwards from what that viewer has seen.
    /// </summary>
    public ulong NextEpoch() => ++_lastEpoch;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWHolonetStateEvent>(OnState);
        SubscribeNetworkEvent<SWHolonetContextRevokedEvent>(OnRevoked);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnAttached(LocalPlayerAttachedEvent ev)
    {
        foreach (var view in _views)
            view.OnActorAttached();
    }

    private void OnDetached(LocalPlayerDetachedEvent ev)
    {
        foreach (var view in _views)
            view.HidePrivateContent();
    }

    public void Register(SWHolonetView view)
    {
        if (!_views.Contains(view))
            _views.Add(view);
    }

    public void Unregister(SWHolonetView view) => _views.Remove(view);

    private void OnState(SWHolonetStateEvent ev)
    {
        foreach (var view in _views)
        {
            if (view.Device == ev.Device)
                view.Apply(ev);
        }
    }

    private void OnRevoked(SWHolonetContextRevokedEvent ev)
    {
        foreach (var view in _views)
        {
            if (view.Device == ev.Device)
                view.OnContextRevoked(ev.Context);
        }
    }
}
