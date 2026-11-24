using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarTravelConsoleBoundUserInterface : BoundUserInterface
{
    private SWGlobalWarTravelConsoleMenu? _menu;

    public SWGlobalWarTravelConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    private long _lastSequence;
    private Guid _subscription;

    protected override void Open()
    {
        base.Open();
        _subscription = Guid.NewGuid();
        _lastSequence = 0;
        EntMan.System<SWGlobalWarMarketViewSystem>().Invalidated += Invalidate;
        _menu = this.CreateWindow<SWGlobalWarTravelConsoleMenu>();
        _menu.OnTravelPressed += OnTravelPressed;
        _menu.OpenCentered();
        // Preserve ordering with the predicted interaction that opens the server-side UI.
        SendPredictedMessage(new SWGlobalWarTravelOpen(_subscription));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (IsOpened && message is SWGlobalWarTravelSnapshot snapshot && _subscription != Guid.Empty &&
            snapshot.Subscription == _subscription && snapshot.Sequence > _lastSequence &&
            EntMan.System<SWGlobalWarMarketViewSystem>().IsCurrentViewer(snapshot.Viewer))
        {
            _lastSequence = snapshot.Sequence;
            _menu?.UpdateState(snapshot.State);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _menu != null)
            _menu.OnTravelPressed -= OnTravelPressed;
        if (disposing)
        {
            _subscription = Guid.Empty;
            EntMan.System<SWGlobalWarMarketViewSystem>().Invalidated -= Invalidate;
        }
        base.Dispose(disposing);
    }

    private void OnTravelPressed(ProtoId<SWGlobalWarPlanetPrototype> destination)
    {
        SendMessage(new SWGlobalWarTravelRequest(destination));
    }
    private void Invalidate() => _menu?.Close();

}
