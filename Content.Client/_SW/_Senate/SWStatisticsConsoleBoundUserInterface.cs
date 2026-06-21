using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate;

public sealed class SWStatisticsConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWStatisticsConsoleMenu? _menu;

    public SWStatisticsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWStatisticsConsoleMenu();
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWStatisticsConsoleState castState)
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
