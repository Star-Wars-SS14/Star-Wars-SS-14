using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarTravelConsoleBoundUserInterface : BoundUserInterface
{
    private SWGlobalWarTravelConsoleMenu? _menu;

    public SWGlobalWarTravelConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWGlobalWarTravelConsoleMenu>();
        _menu.OnTravelPressed += OnTravelPressed;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWGlobalWarTravelConsoleState travel)
            _menu?.UpdateState(travel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _menu != null)
            _menu.OnTravelPressed -= OnTravelPressed;
        base.Dispose(disposing);
    }

    private void OnTravelPressed(ProtoId<SWGlobalWarPlanetPrototype> destination)
    {
        SendMessage(new SWGlobalWarTravelRequest(destination));
    }
}
