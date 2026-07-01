using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.BureauWork;

public sealed class SWBureauWorkConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWBureauWorkConsoleMenu? _menu;

    public SWBureauWorkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWBureauWorkConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SWBureauWorkConsoleState castState)
            _menu?.SetState(castState);
    }

    public void Resign()
    {
        SendMessage(new SWBureauResignMessage());
    }

    public void ResolveDilemma(int optionIndex)
    {
        SendMessage(new SWBureauResolveDilemmaMessage(optionIndex));
    }

    public void ProcessApp(int appId, int action)
    {
        SendMessage(new SWShipsProcessAppMessage(appId, action));
    }

    public void SetPolicy(int level)
    {
        SendMessage(new SWShipsSetPolicyMessage(level));
    }

    public void AuditRequest(int requestId)
    {
        SendMessage(new SWMinistryAuditRequestMessage(requestId));
    }

    public void ResolveRequest(int requestId, int optionIndex)
    {
        SendMessage(new SWMinistryResolveRequestMessage(requestId, optionIndex));
    }

    public void RefuseRequest(int requestId)
    {
        SendMessage(new SWMinistryRefuseRequestMessage(requestId, string.Empty));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
