using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarHubBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarHubMenu? _menu;
    private Guid _subscription;
    private long _sequence;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.NewGuid();
        _sequence = 0;
        EntMan.System<SWGlobalWarMarketViewSystem>().Invalidated += Invalidate;
        _menu = this.CreateWindow<SWGlobalWarHubMenu>();
        _menu.Draft = EntMan.System<SWGlobalWarHubViewSystem>().GetDraft(EntMan.GetNetEntity(Owner));
        _menu.StartRequested += command => SendPredictedMessage(new SWGlobalWarHubStartRequest(command, _subscription));
        _menu.CancelRequested += (generation, revision, attempt) =>
            SendPredictedMessage(new SWGlobalWarHubCancelRequest(generation, revision, attempt, _subscription));
        _menu.GrantRequested += command => SendPredictedMessage(new SWGlobalWarHubGrantRequest(command, _subscription));
        _menu.RevokeRequested += request => SendPredictedMessage(new SWGlobalWarHubRevokeRequest(request.Generation,
            request.Request, request.Revision, request.Grant, request.HeadDecision, _subscription));
        _menu.OpenCentered();
        // Preserve ordering with the predicted interaction that opens the server-side UI.
        SendPredictedMessage(new SWGlobalWarHubOpen(_subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (!IsOpened || _subscription == Guid.Empty)
            return;
        if (message is SWGlobalWarHubSnapshot snapshot && snapshot.Subscription == _subscription &&
            snapshot.Sequence > _sequence && EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer))
        {
            _sequence = snapshot.Sequence;
            _menu?.UpdateState(snapshot.View);
        }
        if (message is SWGlobalWarHubReply reply && reply.Subscription == _subscription &&
            EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(reply.Viewer))
            _menu?.ReceiveReply(reply);
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
