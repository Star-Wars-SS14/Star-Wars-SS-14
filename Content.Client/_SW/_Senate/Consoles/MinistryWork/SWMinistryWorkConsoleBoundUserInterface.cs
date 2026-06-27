using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed class SWMinistryWorkConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWMinistryWorkConsoleMenu? _menu;

    public SWMinistryWorkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWMinistryWorkConsoleMenu();
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWMinistryWorkConsoleState castState)
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
