using Content.Shared._SW.GlobalWar.Combat;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarReconBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarReconMenu? _menu;
    private Guid _subscription;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.NewGuid();
        _menu = this.CreateWindow<SWGlobalWarReconMenu>();
        _menu.OpenCentered();
        SendPredictedMessage(new SWGlobalWarReconOpen(_subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (!IsOpened || message is not SWGlobalWarReconSnapshot snapshot || snapshot.Subscription != _subscription ||
            !EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer)) return;
        _menu?.UpdateState(snapshot);
    }
}
