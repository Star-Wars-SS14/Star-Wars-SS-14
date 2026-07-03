using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.Planner;

public sealed class SWPlannerConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWPlannerConsoleMenu? _menu;

    public SWPlannerConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWPlannerConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWPlannerConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void CreateMeeting(int day, int slot, int duration, string title, string description, string location, bool priv, List<string> invitees)
    {
        SendMessage(new SWPlannerCreateMeetingMessage(day, slot, duration, title, description, location, priv, invitees));
    }

    public void Respond(int meetingId, bool accept, string reason)
    {
        SendMessage(new SWPlannerRespondMessage(meetingId, accept, reason));
    }

    public void Cancel(int meetingId)
    {
        SendMessage(new SWPlannerCancelMessage(meetingId));
    }

    public void Reschedule(int meetingId, int day, int slot, int duration)
    {
        SendMessage(new SWPlannerRescheduleMessage(meetingId, day, slot, duration));
    }

    public void AddBlock(int day, int slot, int duration, string label)
    {
        SendMessage(new SWPlannerBlockMessage(day, slot, duration, label));
    }

    public void RemoveBlock(int blockId)
    {
        SendMessage(new SWPlannerRemoveBlockMessage(blockId));
    }

    public void View(string person, int day)
    {
        SendMessage(new SWPlannerViewMessage(person, day));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
