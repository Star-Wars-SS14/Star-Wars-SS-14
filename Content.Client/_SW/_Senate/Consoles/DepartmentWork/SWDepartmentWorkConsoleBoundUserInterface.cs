using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.DepartmentWork;

public sealed class SWDepartmentWorkConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWDepartmentWorkConsoleMenu? _menu;

    public SWDepartmentWorkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWDepartmentWorkConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWDepartmentWorkConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void Resign()
    {
        SendMessage(new SWBureauResignMessage());
    }

    public void AssumeParent()
    {
        SendMessage(new SWMinistryAssumeParentMessage());
    }

    public void LaunchOp(int op)
    {
        SendMessage(new SWShipsLaunchOpMessage(op));
    }

    public void ResolveTask(int taskId, int optionIndex)
    {
        SendMessage(new SWDeptResolveTaskMessage(taskId, optionIndex));
    }

    public void AdvanceCase(int caseId)
    {
        SendMessage(new SWDeptCaseAdvanceMessage(caseId));
    }

    public void AuditTarget(string targetId)
    {
        SendMessage(new SWDeptAuditTargetMessage(targetId));
    }

    public void SetPost(int post, int delta)
    {
        SendMessage(new SWDeptPostMessage(post, delta));
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
