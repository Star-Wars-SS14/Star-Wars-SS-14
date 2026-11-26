using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.SectorSelect;

public sealed class SWSectorSelectConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWSectorSelectConsoleMenu? _menu;

    public SWSectorSelectConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWSectorSelectConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWSectorSelectConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void SelectSector(string sectorId)
    {
        SendMessage(new SWSelectSectorMessage(sectorId));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
