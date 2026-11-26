using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarMarketConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : SWGlobalWarBoundUserInterface(owner, uiKey)
{
    private SWGlobalWarMarketConsoleMenu? _menu;

    private long _lastSequence;
    private Guid _subscription;
    private Guid _generation;
    private long _revision;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.Empty;
        _lastSequence = 0;
        EntMan.System<SWGlobalWarMarketViewSystem>().Invalidated += Invalidate;
        _menu = this.CreateWindow<SWGlobalWarMarketConsoleMenu>();
        _menu.FlowDraft = EntMan.System<SWGlobalWarMarketViewSystem>().GetDraft(EntMan.GetNetEntity(Owner));
        _menu.DemandRequested += (planet, commodity, units, once, revision) =>
            SendPredictedMessage(new SWGlobalWarDemandRequest(_subscription, _generation, _revision, planet, commodity, units, once, revision));
        _menu.PageRequested += query => SendPredictedMessage(new SWGlobalWarMarketPageRequest(query, _subscription));
        _menu.FlowRequested += command => SendPredictedMessage(new SWGlobalWarFlowRequest(command, _subscription));
        _menu.GrowthRequested += command => SendPredictedMessage(new SWGlobalWarGrowthRequest(command, _subscription));
        _menu.ReturnRequested += command => SendPredictedMessage(new SWGlobalWarReturnRequest(command, _subscription));
        _menu.LanthanumRequested += command => SendPredictedMessage(new SWGlobalWarLanthanumRequest(command, _subscription));
        _menu.SupplyRequested += command => SendPredictedMessage(new SWGlobalWarSupplyRequest(command, _subscription));
        _menu.FlowGrantRequested += command => SendPredictedMessage(new SWGlobalWarFlowGrantRequest(command, _subscription));
        _menu.FlowRevokeRequested += command =>
            SendPredictedMessage(new SWGlobalWarFlowRevokeRequest(command, _subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (IsOpened && message is SWGlobalWarSupplyReply supply && _subscription != Guid.Empty &&
            supply.Subscription == _subscription && EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(supply.Viewer))
            _menu?.ReceiveSupplyResult(supply.Result);
        if (IsOpened && message is SWGlobalWarLanthanumReply lanthanum && _subscription != Guid.Empty &&
            lanthanum.Subscription == _subscription && EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(lanthanum.Viewer))
            _menu?.ReceiveLanthanumResult(lanthanum.Result);
        if (IsOpened && message is SWGlobalWarReturnReply returned && _subscription != Guid.Empty &&
            returned.Subscription == _subscription && EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(returned.Viewer))
            _menu?.ReceiveReturnResult(returned.Result);
        if (IsOpened && message is SWGlobalWarGrowthReply growth && _subscription != Guid.Empty &&
            growth.Subscription == _subscription && EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(growth.Viewer))
            _menu?.ReceiveGrowthResult(growth.Result);
        if (IsOpened && message is SWGlobalWarFlowReply reply && _subscription != Guid.Empty &&
            reply.Subscription == _subscription && EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(reply.Viewer))
            _menu?.ReceiveFlowResult(reply.Result);
        if (IsOpened && message is SWGlobalWarMarketSnapshot snapshot && snapshot.Subscription != Guid.Empty &&
            snapshot.Sequence > _lastSequence &&
            EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer))
        {
            _subscription = snapshot.Subscription;
            _lastSequence = snapshot.Sequence;
            _generation = snapshot.State.Generation;
            _revision = snapshot.State.AssignmentRevision;
            _menu?.UpdateState(snapshot.State);
        }
    }
    private void Invalidate() => _menu?.Close();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _subscription = Guid.Empty;
            EntMan.System<SWGlobalWarMarketViewSystem>().Invalidated -= Invalidate;
        }
        base.Dispose(disposing);
    }
}
