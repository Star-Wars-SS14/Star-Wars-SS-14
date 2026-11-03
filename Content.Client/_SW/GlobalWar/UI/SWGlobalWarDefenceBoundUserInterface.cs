using Content.Shared._SW.GlobalWar.Combat;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarDefenceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarDefenceMenu? _menu;
    private Guid _subscription;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.NewGuid();
        _menu = this.CreateWindow<SWGlobalWarDefenceMenu>();
        _menu.Requested += command => SendPredictedMessage(new SWGlobalWarDefenceRequest(_subscription, command));
        _menu.OpenCentered();
        SendPredictedMessage(new SWGlobalWarDefenceOpen(_subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (!IsOpened || message is not SWGlobalWarDefenceSnapshot snapshot || snapshot.Subscription != _subscription ||
            !EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer))
            return;
        _menu?.UpdateState(snapshot.View);
    }
}
