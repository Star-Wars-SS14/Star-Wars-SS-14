using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.Staff;

public sealed class SWStaffShiftConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWStaffShiftConsoleMenu? _menu;

    public SWStaffShiftConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWStaffShiftConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWStaffShiftConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void Toggle()
    {
        SendMessage(new SWStaffShiftToggleMessage());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
