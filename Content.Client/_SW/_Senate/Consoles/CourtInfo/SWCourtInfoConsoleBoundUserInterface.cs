using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.CourtInfo;

public sealed class SWCourtInfoConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWCourtInfoConsoleMenu? _menu;

    public SWCourtInfoConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWCourtInfoConsoleMenu();
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWCourtInfoConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
