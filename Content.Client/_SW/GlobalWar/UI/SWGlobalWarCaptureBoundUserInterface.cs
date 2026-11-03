using Content.Shared._SW.GlobalWar.Combat;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarCaptureBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarCaptureMenu? _menu;
    private Guid _subscription;
    private SWGlobalWarCaptureView? _view;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.NewGuid();
        _menu = this.CreateWindow<SWGlobalWarCaptureMenu>();
        _menu.Requested += cancel =>
        {
            if (_view is { Generation: var generation } view && generation != Guid.Empty)
                SendPredictedMessage(new SWGlobalWarCaptureRequest(generation, view.AssignmentRevision,
                    Guid.NewGuid(), _subscription, cancel));
        };
        _menu.OpenCentered();
        SendPredictedMessage(new SWGlobalWarCaptureOpen(_subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (!IsOpened || message is not SWGlobalWarCaptureSnapshot snapshot ||
            snapshot.Subscription != _subscription ||
            !EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer))
            return;
        _view = snapshot.View;
        _menu?.UpdateState(snapshot.View);
    }
}
