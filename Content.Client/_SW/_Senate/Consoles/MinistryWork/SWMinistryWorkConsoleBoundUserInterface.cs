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

    public void OrderFleet(int fleetId, SWDefenseFleetOrder order, string target, int eventId = 0)
    {
        SendMessage(new SWDefenseFleetOrderMessage(fleetId, order, target, eventId));
    }

    public void RecruitFleet(string worldId)
    {
        SendMessage(new SWDefenseRecruitMessage(worldId));
    }

    public void BuildWorldDefense(string worldId, string programId)
    {
        SendMessage(new SWDefenseWorldProgramMessage(worldId, programId));
    }

    public void ResupplyWorldDefense(string worldId)
    {
        SendMessage(new SWDefenseWorldResupplyMessage(worldId));
    }

    public void SetPosture(SWSecurityPosture posture)
    {
        SendMessage(new SWSecuritySetPostureMessage(posture));
    }

    public void HealthAction(SWHealthActionMessage message) => SendMessage(message);

    public void SelectEconomyGood(string good) => SendMessage(new SWEconomySelectGoodMessage(good));
    public void FundExpansion(string world, string recipe) => SendMessage(new SWEconomyFundExpansionMessage(world, recipe));
    public void FundEconomyRoute(string edge) => SendMessage(new SWEconomyFundRouteMessage(edge));
    public void RespondEconomyEvent(int eventId, bool accelerated) => SendMessage(new SWEconomyRespondEventMessage(eventId, accelerated));

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

    public void SubmitBudget(int revision, int sector, int reserve, System.Collections.Generic.Dictionary<string, int> weights)
        => SendMessage(new SWBudgetSubmitMessage(revision, sector, reserve, weights));

    public void TransferReserve(int revision, string requestId, bool ministry, string target, int amount, string purpose)
        => SendMessage(new SWBudgetReserveTransferMessage(revision, requestId, ministry, target, amount, purpose));

    public void DecideGovernorRequest(string requestId, bool approve)
        => SendMessage(new SWFinanceGovernorDecisionMessage(requestId, approve));

    public void SetFinanceTax(string id, int rate, bool relief, string requestId)
        => SendMessage(new SWFinanceTaxSetMessage(id, rate, relief, requestId));

    public void OpenJusticeInspection(string institutionId) => SendMessage(new SWJusticeOpenInspectionMessage(institutionId));
    public void RunJusticeCheck(int caseId, string checkId) => SendMessage(new SWJusticeRunCheckMessage(caseId, checkId));
    public void ConcludeJusticeInspection(int caseId, SWJusticeConclusion conclusion, System.Collections.Generic.List<string> evidenceIds)
        => SendMessage(new SWJusticeConcludeMessage(caseId, conclusion, evidenceIds));
    public void ApplyJusticeRemedy(int caseId, SWJusticeRemedy remedy) => SendMessage(new SWJusticeRemedyMessage(caseId, remedy));
    public void FollowUpJusticeInspection(int caseId) => SendMessage(new SWJusticeFollowUpMessage(caseId));

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
    public void SecurityOrder(int teamId, SWSecurityOrder order, string worldId, int caseId = 0)
        => SendMessage(new SWSecurityTeamOrderMessage(teamId, order, worldId, caseId));

    public void SecurityCell(string worldId, bool deploy)
        => SendMessage(new SWSecurityCellCommandMessage(worldId, deploy));
}
