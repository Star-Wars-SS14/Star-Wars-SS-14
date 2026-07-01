using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.Vote;

public sealed class SWSenateVoteConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWSenateVoteConsoleMenu? _menu;

    public SWSenateVoteConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWSenateVoteConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWSenateVoteConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void Vote(SWVoteChoice choice)
    {
        SendMessage(new SWSenateCastVoteMessage(choice));
    }

    public void EndVote()
    {
        SendMessage(new SWSenateEndVoteMessage());
    }

    public void Tiebreak(SWVoteChoice choice)
    {
        SendMessage(new SWSenateTiebreakMessage(choice));
    }

    public void GrantFloor(NetEntity target)
    {
        SendMessage(new SWSenateGrantFloorMessage(target));
    }

    public void RevokeFloor()
    {
        SendMessage(new SWSenateRevokeFloorMessage());
    }

    public void RemoveFromQueue(NetEntity target)
    {
        SendMessage(new SWSenateRemoveFromQueueMessage(target));
    }

    public void GrantChancellor()
    {
        SendMessage(new SWSenateGrantChancellorMessage());
    }

    public void PartyWhip()
    {
        SendMessage(new SWSenatePartyWhipMessage());
    }

    public void Nominate()
    {
        SendMessage(new SWSenateNominateMessage());
    }

    public void ElectionVote(string candidate)
    {
        SendMessage(new SWSenateElectionVoteMessage(candidate));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
