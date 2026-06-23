using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate;

public sealed class SWSenateWorkConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWSenateWorkConsoleMenu? _menu;

    public SWSenateWorkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWSenateWorkConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWSenateWorkConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void TakeOffice()
    {
        SendMessage(new SWSenateTakeOfficeMessage());
    }

    public void Resign()
    {
        SendMessage(new SWSenateResignMessage());
    }

    public void SubmitBill(string name, string description, Dictionary<string, float> effects)
    {
        SendMessage(new SWSenateSubmitBillMessage(name, description, effects));
    }

    public void SubmitPreset(string presetId)
    {
        SendMessage(new SWSenateSubmitPresetMessage(presetId));
    }

    public void ResolveDilemma(int dilemmaId, int optionIndex)
    {
        SendMessage(new SWSenateResolveDilemmaMessage(dilemmaId, optionIndex));
    }

    public void ApplyProgram(string programId)
    {
        SendMessage(new SWSenateApplyProgramMessage(programId));
    }

    public void SetBudget(string sectorId, int amount)
    {
        SendMessage(new SWSenateSetBudgetMessage(sectorId, amount));
    }

    public void LockBudget()
    {
        SendMessage(new SWSenateLockBudgetMessage());
    }

    public void ScheduleSession(int billId, int startHour, int duration)
    {
        SendMessage(new SWSenateScheduleSessionMessage(billId, startHour, duration));
    }

    public void RemoveSession(int sessionId)
    {
        SendMessage(new SWSenateRemoveSessionMessage(sessionId));
    }

    public void ConfirmSession(int sessionId)
    {
        SendMessage(new SWSenateConfirmSessionMessage(sessionId));
    }

    public void RejectSession(int sessionId)
    {
        SendMessage(new SWSenateRejectSessionMessage(sessionId));
    }

    public void Announce(string text)
    {
        SendMessage(new SWSenateAnnounceMessage(text));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
