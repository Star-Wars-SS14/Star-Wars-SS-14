using Content.Shared._SW.GlobalWar.Combat;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarFleetBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarFleetMenu? _menu;
    private Guid _subscription;
    private SWGlobalWarFleetView? _view;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.NewGuid();
        _menu = this.CreateWindow<SWGlobalWarFleetMenu>();
        _menu.Requested += (shipClass, cancel, headDecision) =>
        {
            if (_view is { Generation: var generation } view && generation != Guid.Empty)
                SendPredictedMessage(new SWGlobalWarFleetRequest(generation, view.Revision, Guid.NewGuid(),
                    _subscription, shipClass, cancel, headDecision));
        };
        _menu.OpenCentered();
        SendPredictedMessage(new SWGlobalWarFleetOpen(_subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (!IsOpened || message is not SWGlobalWarFleetSnapshot snapshot || snapshot.Subscription != _subscription ||
            !EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer)) return;
        _view = snapshot.View;
        _menu?.UpdateState(snapshot.View);
    }
}
