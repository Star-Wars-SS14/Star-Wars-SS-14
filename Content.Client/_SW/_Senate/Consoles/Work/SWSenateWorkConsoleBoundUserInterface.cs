using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate.Consoles.Work;

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

    public void ApproveWar()
    {
        SendMessage(new SWDefenseApproveWarMessage());
    }

    public void RejectWar()
    {
        SendMessage(new SWDefenseRejectWarMessage());
    }

    public void ApproveEmergency()
    {
        SendMessage(new SWSecurityApproveEmergencyMessage());
    }

    public void RejectEmergency()
    {
        SendMessage(new SWSecurityRejectEmergencyMessage());
    }

    public void ApproveHealthEmergency()
    {
        SendMessage(new SWHealthApproveEmergencyMessage());
    }

    public void RejectHealthEmergency()
    {
        SendMessage(new SWHealthRejectEmergencyMessage());
    }

    public void ApproveStimulus()
    {
        SendMessage(new SWEconomyApproveStimulusMessage());
    }

    public void RejectStimulus()
    {
        SendMessage(new SWEconomyRejectStimulusMessage());
    }

    public void ApproveEmission()
    {
        SendMessage(new SWFinanceApproveEmissionMessage());
    }

    public void RejectEmission()
    {
        SendMessage(new SWFinanceRejectEmissionMessage());
    }

    public void ApproveTribunal()
    {
        SendMessage(new SWJusticeApproveTribunalMessage());
    }

    public void RejectTribunal()
    {
        SendMessage(new SWJusticeRejectTribunalMessage());
    }

    public void SignBill(int billId)
    {
        SendMessage(new SWSenateSignBillMessage(billId));
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

    public void ResolveCrisis(int crisisId, int solutionIndex)
    {
        SendMessage(new SWSenateResolveCrisisMessage(crisisId, solutionIndex));
    }

    public void AidCrisis(int crisisId)
    {
        SendMessage(new SWSenateAidCrisisMessage(crisisId));
    }

    public void ProposeEmergencyLaw(int crisisId, int lawIndex)
    {
        SendMessage(new SWSenateProposeEmergencyLawMessage(crisisId, lawIndex));
    }

    public void SetBudget(string sectorId, int amount)
    {
        SendMessage(new SWSenateSetBudgetMessage(sectorId, amount));
    }

    public void ApproveBudget()
    {
        SendMessage(new SWBudgetApproveMessage());
    }

    public void RejectBudget(string reason)
    {
        SendMessage(new SWBudgetRejectMessage(reason));
    }

    public void ScheduleSession(int billId, int startHour, int duration)
    {
        SendMessage(new SWSenateScheduleSessionMessage(billId, startHour, duration));
    }

    public void RemoveSession(int sessionId)
    {
        SendMessage(new SWSenateRemoveSessionMessage(sessionId));
    }

    public void EmergencySession(int billId)
    {
        SendMessage(new SWSenateEmergencySessionMessage(billId));
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

    public void CreateParty(string name, string description)
    {
        SendMessage(new SWSenateCreatePartyMessage(name, description));
    }

    public void RequestJoinParty(int partyId)
    {
        SendMessage(new SWSenateRequestJoinPartyMessage(partyId));
    }

    public void CancelJoinParty(int partyId)
    {
        SendMessage(new SWSenateCancelJoinPartyMessage(partyId));
    }

    public void ConfirmJoinParty(int partyId, string sectorId)
    {
        SendMessage(new SWSenateConfirmJoinPartyMessage(partyId, sectorId));
    }

    public void RejectJoinParty(int partyId, string sectorId)
    {
        SendMessage(new SWSenateRejectJoinPartyMessage(partyId, sectorId));
    }

    public void LeaveParty(int partyId)
    {
        SendMessage(new SWSenateLeavePartyMessage(partyId));
    }

    public void DisbandParty(int partyId)
    {
        SendMessage(new SWSenateDisbandPartyMessage(partyId));
    }

    public void InitiateLeaderVote(int partyId)
    {
        SendMessage(new SWSenateInitiateLeaderVoteMessage(partyId));
    }

    public void InitiateEvictVote(int partyId, string sectorId)
    {
        SendMessage(new SWSenateInitiateEvictVoteMessage(partyId, sectorId));
    }

    public void CastLeaderVote(int partyId, bool support)
    {
        SendMessage(new SWSenateCastLeaderVoteMessage(partyId, support));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
