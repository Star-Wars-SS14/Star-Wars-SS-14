using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed class SWMinistryWorkConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWMinistryWorkConsoleMenu? _menu;

    public SWMinistryWorkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWMinistryWorkConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    public void AuditRequest(int requestId)
    {
        SendMessage(new SWMinistryAuditRequestMessage(requestId));
    }

    public void ResolveRequest(int requestId, int optionIndex)
    {
        SendMessage(new SWMinistryResolveRequestMessage(requestId, optionIndex));
    }

    public void RefuseRequest(int requestId, string reason)
    {
        SendMessage(new SWMinistryRefuseRequestMessage(requestId, reason));
    }

    public void Resign()
    {
        SendMessage(new SWMinistryResignMessage());
    }

    public void SetStance(SWDefenseStance stance)
    {
        SendMessage(new SWDefenseSetStanceMessage(stance));
    }

    public void SelectProgram(string programId)
    {
        SendMessage(new SWDefenseSelectProgramMessage(programId));
    }

    public void DeployFleet(int groupId, string target)
    {
        SendMessage(new SWFleetDeployMessage(groupId, target));
    }

    public void LaunchOp(string opId, string target)
    {
        SendMessage(new SWDefenseLaunchOpMessage(opId, target));
    }

    public void SetPosture(SWSecurityPosture posture)
    {
        SendMessage(new SWSecuritySetPostureMessage(posture));
    }

    public void SelectSecProgram(string programId)
    {
        SendMessage(new SWSecuritySelectProgramMessage(programId));
    }

    public void DeployCell(int cellId, string target)
    {
        SendMessage(new SWSecurityDeployCellMessage(cellId, target));
    }

    public void LaunchSecOp(string opId, string target)
    {
        SendMessage(new SWSecurityLaunchOpMessage(opId, target));
    }

    public void RaidPlot(int plotId)
    {
        SendMessage(new SWSecurityRaidPlotMessage(plotId));
    }

    public void SetFunding(int level)
    {
        SendMessage(new SWHealthSetFundingMessage(level));
    }

    public void DeployVaccine()
    {
        SendMessage(new SWHealthDeployVaccineMessage());
    }

    public void Quarantine(string sectorId, bool on)
    {
        SendMessage(new SWHealthQuarantineMessage(sectorId, on));
    }

    public void BuildHospital(string sectorId)
    {
        SendMessage(new SWHealthBuildHospitalMessage(sectorId));
    }

    public void EmergencyAid(string sectorId)
    {
        SendMessage(new SWHealthEmergencyAidMessage(sectorId));
    }

    public void DeclareHealthEmergency()
    {
        SendMessage(new SWHealthDeclareEmergencyMessage());
    }

    public void SelectHealthProgram(string programId)
    {
        SendMessage(new SWHealthSelectProgramMessage(programId));
    }

    public void SetEconPolicy(int level)
    {
        SendMessage(new SWEconomySetPolicyMessage(level));
    }

    public void LaunchProject(string projectId, string target)
    {
        SendMessage(new SWEconomyLaunchProjectMessage(projectId, target));
    }

    public void CancelProject(int index)
    {
        SendMessage(new SWEconomyCancelProjectMessage(index));
    }

    public void AddRoute(string aId, string bId)
    {
        SendMessage(new SWEconomyAddRouteMessage(aId, bId));
    }

    public void RemoveRoute(int routeId)
    {
        SendMessage(new SWEconomyRemoveRouteMessage(routeId));
    }

    public void DeclareStimulus()
    {
        SendMessage(new SWEconomyDeclareStimulusMessage());
    }

    public void SelectEconProgram(string programId)
    {
        SendMessage(new SWEconomySelectProgramMessage(programId));
    }

    public void SetRate(int rate)
    {
        SendMessage(new SWFinanceSetRateMessage(rate));
    }

    public void SetTax(int level)
    {
        SendMessage(new SWFinanceSetTaxMessage(level));
    }

    public void IssueBonds()
    {
        SendMessage(new SWFinanceIssueBondsMessage());
    }

    public void RepayDebt()
    {
        SendMessage(new SWFinanceRepayDebtMessage());
    }

    public void DeclareEmission()
    {
        SendMessage(new SWFinanceEmissionMessage());
    }

    public void SelectFinanceProgram(string programId)
    {
        SendMessage(new SWFinanceSelectProgramMessage(programId));
    }

    public void AdjustBudgetRatio(int deltaPct)
    {
        SendMessage(new SWBudgetAdjustRatioMessage(deltaPct));
    }

    public void SetBudgetWeight(string ministryId, int delta)
    {
        SendMessage(new SWBudgetSetWeightMessage(ministryId, delta));
    }

    public void ResetBudgetWeights()
    {
        SendMessage(new SWBudgetResetWeightsMessage());
    }

    public void SubmitBudget()
    {
        SendMessage(new SWBudgetSubmitMessage());
    }

    public void Investigate(int caseId)
    {
        SendMessage(new SWJusticeInvestigateMessage(caseId));
    }

    public void SendToCourt(int caseId)
    {
        SendMessage(new SWJusticeSendToCourtMessage(caseId));
    }

    public void HoldHearing(int caseId)
    {
        SendMessage(new SWJusticeHoldHearingMessage(caseId));
    }

    public void Verdict(int caseId, SWVerdict verdict)
    {
        SendMessage(new SWJusticeVerdictMessage(caseId, verdict));
    }

    public void SetJusticeStance(SWJusticeStance stance)
    {
        SendMessage(new SWJusticeSetStanceMessage(stance));
    }

    public void SelectJusticeProgram(string programId)
    {
        SendMessage(new SWJusticeSelectProgramMessage(programId));
    }

    public void DeclareTribunal()
    {
        SendMessage(new SWJusticeDeclareTribunalMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWMinistryWorkConsoleState castState)
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
