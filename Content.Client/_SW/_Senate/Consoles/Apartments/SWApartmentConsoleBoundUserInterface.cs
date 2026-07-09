using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.Apartments;

public sealed class SWApartmentConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWApartmentConsoleMenu? _menu;

    public SWApartmentConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWApartmentConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWApartmentConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void Rent(string slotTag)
    {
        SendMessage(new SWApartmentRentMessage(slotTag));
    }

    public void Vacate()
    {
        SendMessage(new SWApartmentVacateMessage());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
