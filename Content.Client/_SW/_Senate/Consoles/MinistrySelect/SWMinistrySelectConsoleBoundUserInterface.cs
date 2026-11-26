using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.MinistrySelect;

public sealed class SWMinistrySelectConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWMinistrySelectConsoleMenu? _menu;

    public SWMinistrySelectConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWMinistrySelectConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWMinistrySelectConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void SelectMinistry(string ministryId)
    {
        SendMessage(new SWSelectMinistryMessage(ministryId));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
