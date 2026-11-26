using Content.Shared._SW.GlobalWar.Combat;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarGunneryBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarGunneryMenu? _menu;
    private Guid _subscription;
    private SWGlobalWarGunnerySnapshot? _snapshot;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.NewGuid();
        _menu = this.CreateWindow<SWGlobalWarGunneryMenu>();
        _menu.Input += (guns, point, firing) =>
        {
            if (_snapshot is { Authorized: true } state)
                SendPredictedMessage(new SWGlobalWarGunneryRequest(state.Generation, state.Revision, _subscription,
                    state.Map, guns, point, firing));
        };
        _menu.OpenCentered();
        SendPredictedMessage(new SWGlobalWarGunneryOpen(_subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (!IsOpened || message is not SWGlobalWarGunnerySnapshot snapshot || snapshot.Subscription != _subscription ||
            !EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer)) return;
        _snapshot = snapshot;
        _menu?.UpdateState(snapshot);
    }
}
