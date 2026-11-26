using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.JudicialWork;

public sealed class SWJudicialWorkConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWJudicialWorkConsoleMenu? _menu;

    public SWJudicialWorkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWJudicialWorkConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWJudicialWorkConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void VerdictVote(int caseId, bool forPlaintiff)
    {
        SendMessage(new SWCourtVerdictVoteMessage(caseId, forPlaintiff));
    }

    public void CloseVote(int caseId)
    {
        SendMessage(new SWCourtCloseVoteMessage(caseId));
    }

    public void StartReview(int lawId)
    {
        SendMessage(new SWCourtStartReviewMessage(lawId));
    }

    public void ReviewVote(bool unconstitutional)
    {
        SendMessage(new SWCourtReviewVoteMessage(unconstitutional));
    }

    public void Resign()
    {
        SendMessage(new SWCourtResignMessage());
    }

    public void TakeOffice()
    {
        SendMessage(new SWSenateTakeOfficeMessage());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
