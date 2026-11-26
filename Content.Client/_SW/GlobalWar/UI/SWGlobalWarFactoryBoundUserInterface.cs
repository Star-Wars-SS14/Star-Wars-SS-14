using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarFactoryBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarFactoryMenu? _menu;
    private SWGlobalWarFactoryState? _state;
    private Guid _subscription;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.NewGuid();
        _menu = this.CreateWindow<SWGlobalWarFactoryMenu>();
        _menu.Requested += (action, kind, item, selection) =>
        {
            if (_state is { } state)
                SendPredictedMessage(new SWGlobalWarFactoryRequest(state.Generation, Guid.NewGuid(), state.Revision,
                    action, kind, item, selection));
        };
        _menu.OpenCentered();
        SendPredictedMessage(new SWGlobalWarFactoryOpen(_subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (IsOpened && message is SWGlobalWarFactorySnapshot snapshot && snapshot.Subscription == _subscription &&
            EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer))
        {
            _state = snapshot.State;
            _menu?.UpdateState(snapshot.State);
        }
    }
}
