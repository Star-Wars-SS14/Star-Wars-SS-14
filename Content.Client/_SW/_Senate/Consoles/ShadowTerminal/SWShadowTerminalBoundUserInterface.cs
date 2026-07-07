using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.ShadowTerminal;

public sealed class SWShadowTerminalBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWShadowTerminalMenu? _menu;

    public SWShadowTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWShadowTerminalMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWShadowTerminalState castState)
            return;

        _menu?.SetState(castState);
    }

    public void Hire(SWMercContractKind kind, string target)
    {
        SendMessage(new SWShadowHireMessage(kind, target));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
